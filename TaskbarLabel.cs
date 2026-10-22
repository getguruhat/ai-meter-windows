using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;
using Timer = System.Windows.Forms.Timer;

namespace AIMeter;

/// "AI 79%" on the taskbar, just left of the notification area, like the macOS menu bar
/// title. A tray icon is always square, so the text is its own small window kept above
/// the taskbar. When the taskbar or its tray can't be found, or the taskbar is vertical,
/// auto-hidden, or under a full-screen app, the label hides and <see cref="ShowingChanged"/>
/// lets the tray icon take over.
internal sealed class TaskbarLabel : Form
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref Point dst, ref Size size, IntPtr srcDc, ref Point src, int key, ref BlendFunction blend, int flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    private static readonly IntPtr Topmost = new(-1);
    private const uint NoSize = 0x1, NoMove = 0x2, NoActivate = 0x10;

    private static readonly string Family = FindFamily();
    private readonly Timer follow = new() { Interval = 1000 };
    private readonly ToolTip tip = new();
    private string text = "AI –";
    private bool on, showing;

    public event Action? LeftClicked;
    public event Action? RightClicked;
    /// Raised when the label appears or disappears, so the tray icon can stand in for it.
    public event Action<bool>? ShowingChanged;

    /// Where the label is on screen, for opening the panel next to it.
    public Rectangle Spot { get; private set; }

    public TaskbarLabel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "AI Meter";
        follow.Tick += (_, _) => Follow();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_LAYERED (per-pixel alpha), WS_EX_TOOLWINDOW (not in Alt+Tab),
            // WS_EX_NOACTIVATE (clicking it doesn't take focus), WS_EX_TOPMOST.
            cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000008;
            return cp;
        }
    }

    public bool On
    {
        get => on;
        set { on = value; follow.Enabled = value; Follow(); }
    }

    public void SetText(string label, string tooltip)
    {
        text = label;
        tip.SetToolTip(this, tooltip);
        Follow();
    }

    /// Re-checks where the taskbar and tray are (they move, resize, and restart with
    /// Explorer), redraws the label there, and keeps it above the taskbar.
    private void Follow()
    {
        if (on && FindSpot() is { } spot)
        {
            var scale = spot.Dpi / 96f;
            using var bitmap = Draw(text, scale, spot.Bar.Height, MeterIcon.LightTaskbar);
            var at = new Point(spot.TrayLeft - bitmap.Width - (int)Math.Round(4 * scale), spot.Bar.Top);
            Spot = new Rectangle(at, bitmap.Size);
            Bounds = Spot;
            Present(bitmap, at);
            if (!Visible) Show();
            SetWindowPos(Handle, Topmost, 0, 0, 0, 0, NoMove | NoSize | NoActivate);
        }
        else if (Visible) Hide();
        if (showing == Visible) return;
        showing = Visible;
        ShowingChanged?.Invoke(showing);
    }

    private static (Rectangle Bar, int TrayLeft, uint Dpi)? FindSpot()
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero || !IsWindowVisible(bar) || !GetWindowRect(bar, out var b)) return null;
        var tray = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var t)) return null;
        var barRect = Rectangle.FromLTRB(b.Left, b.Top, b.Right, b.Bottom);
        var screen = Screen.FromHandle(bar).Bounds;
        // Only a horizontal taskbar that's fully on screen (an auto-hidden one slides off).
        if (barRect.Width <= barRect.Height || barRect.Top < screen.Top || barRect.Bottom > screen.Bottom) return null;
        if (t.Left <= barRect.Left + barRect.Width / 3 || t.Left > barRect.Right) return null;
        if (FullScreenApp(bar, screen)) return null;
        var dpi = GetDpiForWindow(bar);
        return (barRect, t.Left, dpi == 0 ? 96 : dpi);
    }

    /// A video or game covering the whole screen hides the taskbar; hide the label too.
    private static bool FullScreenApp(IntPtr bar, Rectangle screen)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || window == bar || !GetWindowRect(window, out var r)) return false;
        if (!Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).Contains(screen)) return false;
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString() is not ("Progman" or "WorkerW");
    }

    /// Draws the label: thin text centered in a strip as tall as the taskbar. The rest is
    /// nearly (not fully) transparent, so clicks anywhere on the label reach it.
    public static Bitmap Draw(string label, float scale, int height, bool light)
    {
        using var family = new FontFamily(Family);
        using var path = new GraphicsPath();
        // Filled as a shape: text drawn onto a transparent bitmap gets dark edges.
        path.AddString(label, family, (int)FontStyle.Regular, 13 * scale, Point.Empty, StringFormat.GenericTypographic);
        var bounds = path.GetBounds();
        var pad = (int)Math.Round(6 * scale);
        var bitmap = new Bitmap((int)Math.Ceiling(bounds.Width) + pad * 2, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(1, 0, 0, 0));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var center = new Matrix())
        {
            center.Translate(pad - bounds.X, MathF.Round((height - bounds.Height) / 2 - bounds.Y));
            path.Transform(center);
        }
        using var brush = new SolidBrush(light ? Color.Black : Color.White);
        g.FillPath(brush, path);
        return bitmap;
    }

    /// Windows 11's own taskbar text uses Segoe UI Variable; Windows 10 has Segoe UI.
    private static string FindFamily()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
    }

    private void Present(Bitmap bitmap, Point at)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var handle = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = SelectObject(memoryDc, handle);
        try
        {
            var size = bitmap.Size;
            var origin = Point.Empty;
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_ALPHA
            UpdateLayeredWindow(Handle, screenDc, ref at, ref size, memoryDc, ref origin, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            SelectObject(memoryDc, old);
            DeleteObject(handle);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) LeftClicked?.Invoke();
        else if (e.Button == MouseButtons.Right) RightClicked?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { follow.Dispose(); tip.Dispose(); }
        base.Dispose(disposing);
    }
}
