using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AIMeter;

/// AI Meter's settings, opened from the gear in the panel: the app icon, name, and version,
/// a "Start with Windows" switch, and a Support link. One window is reused; showing it
/// again brings it to the front.
internal sealed class SettingsWindow : Form
{
    private const int ContentWidth = 390;
    private const TextFormatFlags Centered = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter;
    private const TextFormatFlags NoPad = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// "1.0.4", from the Version in AIMeter.csproj.
    public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

    private static SettingsWindow? shared;

    private readonly Action support;
    private readonly Image? icon;
    private readonly List<(Rectangle Rect, Action Click)> targets = [];
    private Theme theme = Theme.Light;
    private Font? name, body, strong, small, glyph;
    private float scale;
    private int hover = -1;
    private bool startup, managed;

    public SettingsWindow(Action support)
    {
        this.support = support;
        // Embedded in AIMeter.csproj, so the icon stays sharp at 88 px.
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AIMeter.png"))
            icon = stream is null ? null : Image.FromStream(stream);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = Strings.Current.Settings;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Relayout();
    }

    public static void ShowShared(Action support)
    {
        if (shared is null || shared.IsDisposed)
        {
            shared = new SettingsWindow(support);
            shared.Show();
        }
        if (shared.WindowState == FormWindowState.Minimized) shared.WindowState = FormWindowState.Normal;
        shared.Activate();
    }

    /// Draws the window's content for the smoke test's screenshots, without showing it.
    public Bitmap Preview(Theme look, bool startupOn, bool managedByWindows)
    {
        Relayout(look, (startupOn, managedByWindows));
        var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        Render(g);
        return bitmap;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Relayout();
    }

    /// The switch can also be changed from the right-click menu or Windows' own Startup
    /// apps settings, so it's read again whenever the window comes to the front.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Relayout();
    }

    private void Relayout(Theme? look = null, (bool On, bool Managed)? state = null)
    {
        theme = look ?? Theme.Current;
        (startup, managed) = state ?? (Startup.Enabled, Startup.ManagedByWindows);
        BackColor = theme.Back;
        if (IsHandleCreated)
        {
            // A dark title bar in dark mode (Windows 10 20H1 and later; ignored before).
            var dark = theme == Theme.Dark ? 1 : 0;
            DwmSetWindowAttribute(Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        }
        if (DeviceDpi / 96f != scale)
        {
            scale = DeviceDpi / 96f;
            foreach (var font in new[] { name, body, strong, small, glyph }) font?.Dispose();
            Font Make(string family, float px) => new(family, px * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            name = new Font("Segoe UI", 22 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            body = Make("Segoe UI", 15);
            strong = Make("Segoe UI Semibold", 15);
            small = Make("Segoe UI", 12);
            glyph = Make("Segoe UI Symbol", 22);
        }
        ClientSize = new Size(S(ContentWidth), Draw(null) + S(34));
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e) => Render(e.Graphics);

    private void Render(Graphics g)
    {
        g.Clear(theme.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        Draw(g);
    }

    private int S(float value) => (int)Math.Round(value * scale);

    /// Lays out (and, given a Graphics, paints) the window; returns where its content ends.
    private int Draw(Graphics? g)
    {
        targets.Clear();
        var text = Strings.Current;
        var width = S(ContentWidth);
        var pad = S(28);
        var y = S(40);

        if (icon is not null)
        {
            var size = S(88);
            g?.DrawImage(icon, new Rectangle((width - size) / 2, y, size, size));
            y += size + S(16);
        }
        y += Centre(g, "AI Meter", name!, theme.Text, y) + S(12);
        y += Centre(g, string.Format(text.Version, Version), body!, theme.Secondary, y) + S(14);
        y += Centre(g, text.Tagline, strong!, theme.Text, y) + S(22);
        y = Rule(g, pad, y, width) + S(18);

        // "Start with Windows" and its switch. While Windows' own settings hold it on or off,
        // the switch is dimmed and a link opens those settings, where only the user can change it.
        var accent = theme == Theme.Dark ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 95, 184);
        var labelSize = TextRenderer.MeasureText(text.StartWithWindows, body, Size.Empty, NoPad);
        var knob = new Size(S(40), S(20));
        var row = new Rectangle(pad - S(8), y - S(8), width - (pad - S(8)) * 2, Math.Max(labelSize.Height, knob.Height) + S(16));
        // Always the first target, so the hover and cursor code can tell it apart.
        if (hover == Target(row, () => { if (!managed) Toggle(); }) && !managed) Fill(g, row, theme.Hover, S(8));
        Write(g, text.StartWithWindows, body!, new Rectangle(pad, row.Y + (row.Height - labelSize.Height) / 2, labelSize.Width, labelSize.Height), managed ? theme.Secondary : theme.Text, NoPad);
        Switch(g, new Rectangle(width - pad - knob.Width, row.Y + (row.Height - knob.Height) / 2, knob.Width, knob.Height), startup, managed);
        y = row.Bottom;
        if (managed)
        {
            y += S(2);
            y += Line(g, startup ? text.StartupOnInWindows : text.StartupOffInWindows, small!, theme.Secondary, pad, y, width - pad * 2) + S(4);
            var linkSize = TextRenderer.MeasureText(text.OpenStartupApps, small, Size.Empty, NoPad);
            var link = new Rectangle(pad, y, linkSize.Width, linkSize.Height);
            Write(g, text.OpenStartupApps, small!, link, accent, NoPad);
            if (hover == Target(link, OpenStartupApps)) Underline(g, link, accent);
            y = link.Bottom;
        }
        y = Rule(g, pad, y + S(18), width) + S(22);

        // "♥ Support AI Meter" as a link in the accent blue, underlined while hovered.
        const string heart = "♥";
        var heartSize = TextRenderer.MeasureText(heart, glyph, Size.Empty, TextFormatFlags.NoPadding);
        var supportSize = TextRenderer.MeasureText(text.Support, body, Size.Empty, TextFormatFlags.NoPadding);
        var gap = S(10);
        var total = heartSize.Width + gap + supportSize.Width;
        var height = Math.Max(heartSize.Height, supportSize.Height);
        var supportLink = new Rectangle((width - total) / 2, y, total, height);
        Write(g, heart, glyph!, new Rectangle(supportLink.X, y + (height - heartSize.Height) / 2, heartSize.Width, heartSize.Height), accent, TextFormatFlags.NoPadding);
        var labelAt = new Rectangle(supportLink.X + heartSize.Width + gap, y + (height - supportSize.Height) / 2, supportSize.Width, supportSize.Height);
        Write(g, text.Support, body!, labelAt, accent, TextFormatFlags.NoPadding);
        if (hover == Target(supportLink, support)) Underline(g, labelAt, accent);
        y = supportLink.Bottom + S(18);
        return y + Centre(g, text.Thanks, small!, theme.Secondary, y);
    }

    /// An on/off switch like Windows 11's: a filled pill with a white knob on the right
    /// when on, an outlined pill with a knob on the left when off. Dimmed when disabled.
    private void Switch(Graphics? g, Rectangle track, bool on, bool disabled)
    {
        if (g is null) return;
        var accent = theme.Accent;
        var off = theme.Secondary;
        if (disabled) { accent = Color.FromArgb(110, accent); off = Color.FromArgb(110, off); }
        var dot = track.Height - S(on ? 8 : 10);
        var dotRect = new Rectangle(on ? track.Right - S(5) - dot : track.X + S(5), track.Y + (track.Height - dot) / 2, dot, dot);
        if (on)
        {
            Fill(g, track, accent, track.Height / 2);
            Fill(g, dotRect, Color.White, dot / 2);
        }
        else
        {
            using (var path = Round(new Rectangle(track.X, track.Y, track.Width - 1, track.Height - 1), track.Height / 2))
            using (var pen = new Pen(off, Math.Max(1, scale)))
                g.DrawPath(pen, path);
            Fill(g, dotRect, off, dot / 2);
        }
    }

    private void Toggle()
    {
        Startup.Enabled = !startup;
        Relayout();
    }

    private static void OpenStartupApps() => Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });

    private int Target(Rectangle rect, Action click)
    {
        targets.Add((rect, click));
        return targets.Count - 1;
    }

    /// A hairline across the window; returns y.
    private int Rule(Graphics? g, int pad, int y, int width)
    {
        if (g is not null) { using var pen = new Pen(theme.Line, Math.Max(1, scale)); g.DrawLine(pen, pad, y, width - pad, y); }
        return y;
    }

    /// Writes one centered line at y and returns its height.
    private int Centre(Graphics? g, string text, Font font, Color color, int y)
    {
        var height = TextRenderer.MeasureText(text, font, Size.Empty, Centered).Height;
        Write(g, text, font, new Rectangle(0, y, S(ContentWidth), height), color, Centered);
        return height;
    }

    /// Writes wrapped text at (x, y) and returns its height.
    private static int Line(Graphics? g, string text, Font font, Color color, int x, int y, int width)
    {
        var flags = NoPad | TextFormatFlags.WordBreak;
        var height = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), flags).Height;
        Write(g, text, font, new Rectangle(x, y, width, height), color, flags);
        return height;
    }

    private static void Write(Graphics? g, string text, Font font, Rectangle rect, Color color, TextFormatFlags flags)
    {
        if (g is not null) TextRenderer.DrawText(g, text, font, rect, color, flags);
    }

    private void Underline(Graphics? g, Rectangle text, Color color)
    {
        if (g is null) return;
        using var pen = new Pen(color, Math.Max(1, scale));
        g.DrawLine(pen, text.X, text.Bottom, text.Right, text.Bottom);
    }

    private static GraphicsPath Round(Rectangle rect, int radius)
    {
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void Fill(Graphics? g, Rectangle rect, Color color, int radius)
    {
        if (g is null) return;
        using var path = Round(rect, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = targets.FindIndex(t => t.Rect.Contains(e.Location));
        if (index == hover) return;
        hover = index;
        Cursor = index >= 0 && !(managed && index == 0) ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var index = targets.FindIndex(t => t.Rect.Contains(e.Location));
        if (index >= 0) targets[index].Click();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            icon?.Dispose();
            foreach (var font in new[] { name, body, strong, small, glyph }) font?.Dispose();
        }
        base.Dispose(disposing);
    }
}
