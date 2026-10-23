using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Timer = System.Windows.Forms.Timer;

namespace AIMeter;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--smoke-test")) return SmokeTest.Run();
        using var mutex = new Mutex(true, "GuruHat.AIMeter.Windows", out var first);
        if (!first) return 0;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MeterContext());
        return 0;
    }
}

internal sealed class CachedReading
{
    public int Remaining { get; set; }
    public DateTime? Reset { get; set; }
    public DateTime? Updated { get; set; }
}

/// The selected provider plus the last good reading for each one, so a failed or
/// rate-limited request still shows the most recent value instead of "unavailable".
internal sealed class Settings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private string path = "";

    public Provider Provider { get; set; } = Provider.Codex;
    /// Show "AI 79%" on the taskbar instead of the square tray icon.
    public bool TaskbarText { get; set; } = true;
    public Dictionary<string, CachedReading> Cache { get; set; } = [];

    public static Settings Load(string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GuruHat", "AIMeter");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "settings.json");
        Settings? settings = null;
        try { settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file), Json); } catch (IOException) { } catch (JsonException) { }
        settings ??= new Settings();
        settings.path = file;
        return settings;
    }

    public (Reading? Reading, DateTime? Updated) Cached(Provider provider) =>
        Cache.TryGetValue(provider.ToString(), out var c) ? (new Reading(c.Remaining, c.Reset), c.Updated) : (null, null);

    public void Store(Provider provider, Reading? reading, DateTime? updated = null)
    {
        if (reading is null) Cache.Remove(provider.ToString());
        else Cache[provider.ToString()] = new CachedReading { Remaining = reading.Remaining, Reset = reading.Reset, Updated = updated };
        Save();
    }

    public void Save()
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(this, Json)); } catch (IOException) { }
    }
}

/// "Start with Windows": a Run registry value for the installer version, and the package's
/// startup task for the Microsoft Store (MSIX) version, where Windows ignores Run values
/// the app writes.
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GuruHat AI Meter";
    /// Matches the StartupTask TaskId in msix/AppxManifest.xml.
    private const string TaskId = "AIMeterStartup";
    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);

    public static readonly bool Packaged = IsPackaged();

    private static bool IsPackaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != NoPackage;
    }

    public static bool Enabled
    {
        get
        {
            if (Packaged) return GetTask().State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        set
        {
            if (Packaged)
            {
                var task = GetTask();
                if (!value) { task.Disable(); return; }
                var state = Task.Run(async () => await task.RequestEnableAsync()).GetAwaiter().GetResult();
                // Once the user turns AI Meter off in Windows' own startup settings, only they can turn it back on.
                if (state == StartupTaskState.DisabledByUser)
                    MessageBox.Show("Windows has AI Meter turned off at startup. Turn it on in Settings › Apps › Startup.", "AI Meter");
                return;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
            else key.DeleteValue(ValueName, false);
        }
    }

    private static StartupTask GetTask() =>
        Task.Run(async () => await StartupTask.GetAsync(TaskId)).GetAwaiter().GetResult();
}

/// Draws the tray icon: the remaining percentage as plain text on a clear background,
/// black on a light taskbar and white on a dark one.
internal static class MeterIcon
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// Bar color in the panel: green, then amber below 50%, red below 20%.
    public static Color ColorFor(int? remaining) => remaining switch
    {
        null => Color.FromArgb(107, 114, 128),
        >= 50 => Color.FromArgb(34, 139, 74),
        >= 20 => Color.FromArgb(214, 137, 16),
        _ => Color.FromArgb(205, 50, 40)
    };

    /// The taskbar follows the Windows "system" mode, not the "app" mode the panel uses.
    public static bool LightTaskbar
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not 0;
        }
    }

    public static Bitmap Draw(int? remaining, int size, bool light)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        // The digits are filled as a shape: text drawn onto a transparent bitmap blends its
        // edges toward black, which leaves white digits with a dark fringe.
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var text = remaining?.ToString() ?? "–";
        // Semibold: the regular weight is too faint at 16 px. The tray icon only shows when
        // the taskbar label can't.
        using var family = new FontFamily("Segoe UI Semibold");
        // Shrink until the number fits, so "100" stays readable at 16 px.
        var emSize = size * 1.1f;
        GraphicsPath path;
        while (true)
        {
            path = new GraphicsPath();
            path.AddString(text, family, (int)FontStyle.Regular, emSize, Point.Empty, StringFormat.GenericTypographic);
            var fit = path.GetBounds();
            if ((fit.Width <= size && fit.Height <= size * 0.8f) || emSize <= size * 0.4f) break;
            path.Dispose();
            emSize -= 0.5f;
        }
        using (path)
        using (var brush = new SolidBrush(light ? Color.Black : Color.White))
        {
            var bounds = path.GetBounds();
            using var center = new Matrix();
            center.Translate((float)Math.Round((size - bounds.Width) / 2 - bounds.X), (float)Math.Round((size - bounds.Height) / 2 - bounds.Y));
            path.Transform(center);
            g.FillPath(brush, path);
        }
        return bitmap;
    }

    /// Returns an icon the caller owns; dispose it with <see cref="Release"/>.
    public static Icon Create(int? remaining)
    {
        using var bitmap = Draw(remaining, SystemInformation.SmallIconSize.Width, LightTaskbar);
        return Icon.FromHandle(bitmap.GetHicon());
    }

    public static void Release(Icon? icon)
    {
        if (icon is null) return;
        var handle = icon.Handle;
        icon.Dispose();
        DestroyIcon(handle);
    }
}

internal sealed class MeterContext : ApplicationContext
{
    private const string WebsiteUrl = "https://guruhat.com/apps/ai-meter/";
    private const string DonationUrl = "https://www.paypal.com/donate/?hosted_button_id=FHGYWD46722JS";

    private readonly Settings settings = Settings.Load();
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem startup = new("Start with Windows");
    private readonly ToolStripMenuItem taskbarText = new("Show AI % on the taskbar");
    private readonly MeterPanel panel;
    private readonly TaskbarLabel label = new();
    private readonly Timer timer = new() { Interval = 5 * 60 * 1000 };
    private Reading? reading;
    private string? problem;
    /// When a provider asked us to back off, no request is sent to it before this time.
    private readonly Dictionary<Provider, DateTime> retryAt = [];
    private Icon? icon;
    private bool refreshing;

    public MeterContext()
    {
        reading = settings.Cached(settings.Provider).Reading;
        panel = new MeterPanel(Select, () => _ = RefreshAsync(), () => Open(WebsiteUrl), () => Open(DonationUrl), Quit);
        startup.Click += (_, _) => { Startup.Enabled = !Startup.Enabled; startup.Checked = Startup.Enabled; };
        taskbarText.Click += (_, _) =>
        {
            settings.TaskbarText = !settings.TaskbarText;
            settings.Save();
            label.On = settings.TaskbarText;
        };
        // Right-click keeps a short Windows menu for the settings the panel doesn't show.
        menu.Items.AddRange([
            new ToolStripMenuItem("Refresh now", null, async (_, _) => await RefreshAsync()),
            taskbarText,
            startup,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Quit", null, (_, _) => Quit())
        ]);
        menu.Opening += (_, _) => { startup.Checked = Startup.Enabled; taskbarText.Checked = settings.TaskbarText; };
        tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        // Left-click opens the usage panel, like the macOS menu bar item.
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) panel.Toggle(Cursor.Position);
        };
        // While "AI 79%" is on the taskbar it stands in for the tray icon, like the macOS
        // menu bar title; the tray icon comes back whenever the label can't be shown.
        label.ShowingChanged += showing => tray.Visible = !showing;
        label.LeftClicked += () => panel.Toggle(new Point(label.Spot.X + label.Spot.Width / 2, label.Spot.Y));
        label.RightClicked += () =>
        {
            menu.Show(Cursor.Position);
            // Lets the menu close when you click elsewhere, as it does from the tray.
            SetForegroundWindow(menu.Handle);
        };
        Render();
        label.On = settings.TaskbarText;
        // Redraw the icon when the taskbar switches between light and dark.
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();
        _ = RefreshAsync();
    }

    private void Select(Provider provider)
    {
        if (settings.Provider == provider) return;
        settings.Provider = provider;
        settings.Save();
        reading = settings.Cached(provider).Reading;
        problem = null;
        Render();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            var provider = settings.Provider;
            if (retryAt.TryGetValue(provider, out var until) && until > DateTime.Now)
            {
                problem = Usage.RateLimitText(provider, until);
                Render();
                return;
            }
            var outcome = await Usage.FetchAsync(provider);
            if (provider != settings.Provider) return;
            if (outcome.Reading is { } result)
            {
                reading = result;
                problem = null;
                retryAt.Remove(provider);
                settings.Store(provider, result, DateTime.Now);
            }
            else
            {
                problem = outcome.Problem;
                if (outcome.RetryAt is { } next) retryAt[provider] = next;
                // A failed check keeps the last good reading, unless its window has already reset.
                if (reading?.Reset is { } r && r < DateTime.Now) { reading = null; settings.Store(provider, null); }
            }
            Render();
        }
        finally { refreshing = false; }
    }

    private void Render()
    {
        panel.SetState(new PanelState(settings.Provider, reading, problem));
        var summary = reading is null ? "unavailable" : $"{reading.Remaining}% left";
        if (reading?.Reset is { } when) summary += $", resets {Usage.ResetText(when, DateTime.Now).At}";
        var tip = $"AI Meter · {settings.Provider}: {summary}";
        tray.Text = tip.Length > 127 ? tip.Substring(0, 127) : tip;
        label.SetText(reading is null ? "AI –" : $"AI {reading.Remaining}%", tip);
        var old = icon;
        icon = MeterIcon.Create(reading?.Remaining);
        tray.Icon = icon;
        MeterIcon.Release(old);
    }

    private void OnPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) Render();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void Quit()
    {
        timer.Stop();
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        panel.Dispose();
        label.Dispose();
        tray.Visible = false;
        tray.Dispose();
        MeterIcon.Release(icon);
        ExitThread();
    }
}
