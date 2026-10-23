using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AIMeter;

/// "About AI Meter", like the macOS About window: the app icon, name, version, and a
/// Support link. One window is reused; showing it again brings it to the front.
internal sealed class AboutWindow : Form
{
    private const int ContentWidth = 390, ContentHeight = 430;
    private const TextFormatFlags Centered = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// "1.0.3", from the Version in AIMeter.csproj.
    public static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

    private static AboutWindow? shared;

    private readonly Action support;
    private readonly Image? icon;
    private Theme theme = Theme.Light;
    private Font? name, body, strong, small, glyph;
    private float scale;
    private Rectangle link;
    private bool hovering;

    public AboutWindow(Action support)
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
        Text = "About AI Meter";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Relayout();
    }

    public static void ShowShared(Action support)
    {
        if (shared is null || shared.IsDisposed)
        {
            shared = new AboutWindow(support);
            shared.Show();
        }
        if (shared.WindowState == FormWindowState.Minimized) shared.WindowState = FormWindowState.Normal;
        shared.Activate();
    }

    /// Draws the window's content for the smoke test's screenshots, without showing it.
    public Bitmap Preview(Theme look)
    {
        Relayout(look);
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

    private void Relayout(Theme? look = null)
    {
        theme = look ?? Theme.Current;
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
            glyph = Make("Segoe UI Symbol", 15);
        }
        ClientSize = new Size(S(ContentWidth), S(ContentHeight));
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e) => Render(e.Graphics);

    private int S(float value) => (int)Math.Round(value * scale);

    private void Render(Graphics g)
    {
        g.Clear(theme.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var width = S(ContentWidth);
        var pad = S(28);
        var y = S(54);

        if (icon is not null)
        {
            var size = S(88);
            g.DrawImage(icon, new Rectangle((width - size) / 2, y, size, size));
            y += size + S(16);
        }
        y += Centre(g, "AI Meter", name!, theme.Text, y) + S(12);
        y += Centre(g, $"Version {Version}", body!, theme.Secondary, y) + S(14);
        y += Centre(g, "A GuruHat open-source tool", strong!, theme.Text, y) + S(22);

        using (var pen = new Pen(theme.Line, Math.Max(1, scale))) g.DrawLine(pen, pad, y, width - pad, y);
        y += S(22);

        // "♥ Support AI Meter" as a link in the accent blue, underlined while hovered.
        var accent = theme == Theme.Dark ? Color.FromArgb(76, 194, 255) : Color.FromArgb(0, 95, 184);
        const string heart = "♥", label = "Support AI Meter";
        var heartSize = TextRenderer.MeasureText(heart, glyph, Size.Empty, TextFormatFlags.NoPadding);
        var labelSize = TextRenderer.MeasureText(label, body, Size.Empty, TextFormatFlags.NoPadding);
        var gap = S(10);
        var total = heartSize.Width + gap + labelSize.Width;
        link = new Rectangle((width - total) / 2, y, total, Math.Max(heartSize.Height, labelSize.Height));
        TextRenderer.DrawText(g, heart, glyph, new Point(link.X, y), accent, TextFormatFlags.NoPadding);
        var labelAt = new Point(link.X + heartSize.Width + gap, y);
        TextRenderer.DrawText(g, label, body, labelAt, accent, TextFormatFlags.NoPadding);
        if (hovering)
        {
            using var pen = new Pen(accent, Math.Max(1, scale));
            g.DrawLine(pen, labelAt.X, labelAt.Y + labelSize.Height, labelAt.X + labelSize.Width, labelAt.Y + labelSize.Height);
        }
        y = link.Bottom + S(18);
        Centre(g, "Thank you for supporting this project!", small!, theme.Secondary, y);
    }

    /// Writes one centered line at y and returns its height.
    private int Centre(Graphics g, string text, Font font, Color color, int y)
    {
        var height = TextRenderer.MeasureText(text, font, Size.Empty, Centered).Height;
        TextRenderer.DrawText(g, text, font, new Rectangle(0, y, S(ContentWidth), height), color, Centered);
        return height;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var over = link.Contains(e.Location);
        if (over == hovering) return;
        hovering = over;
        Cursor = over ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && link.Contains(e.Location)) support();
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
