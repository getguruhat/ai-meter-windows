using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace AIMeter;

internal sealed record PanelState(Provider Provider, Reading? Reading, string? Problem, bool SignIn = false);

/// Panel colors, following the Windows "app mode" light/dark setting.
internal sealed record Theme(Color Back, Color Text, Color Secondary, Color Track, Color Selected, Color Hover, Color Line, Color Warning)
{
    public static readonly Theme Light = new(Color.FromArgb(249, 249, 249), Color.FromArgb(27, 27, 27), Color.FromArgb(96, 96, 96),
        Color.FromArgb(228, 228, 228), Color.White, Color.FromArgb(236, 236, 236), Color.FromArgb(220, 220, 220), Color.FromArgb(176, 92, 0));
    public static readonly Theme Dark = new(Color.FromArgb(44, 44, 44), Color.White, Color.FromArgb(168, 168, 168),
        Color.FromArgb(62, 62, 62), Color.FromArgb(92, 92, 92), Color.FromArgb(58, 58, 58), Color.FromArgb(68, 68, 68), Color.FromArgb(255, 159, 10));

    public static Theme Current
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is 0 ? Dark : Light;
        }
    }
}

/// The window that opens from the tray icon, laid out like the macOS menu bar window:
/// a Codex/Claude switch, what's left, when it resets, any problem, then icon buttons.
/// Everything is painted by <see cref="Draw"/>, which also measures the layout when
/// called without a Graphics.
internal sealed class MeterPanel : Form
{
    private const int PanelWidth = 264;
    private const TextFormatFlags NoPad = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private readonly Action<Provider> select;
    private readonly Action troubleshooting;
    private readonly (string Glyph, string Tip, Action Click)[] buttons;
    private readonly List<(Rectangle Rect, string Tip, Action Click)> targets = [];
    private readonly ToolTip tip = new();
    private readonly Timer clock = new() { Interval = 30_000 };
    private readonly string iconFamily;
    private PanelState state = new(Provider.Codex, null, null);
    private Theme theme = Theme.Light;
    private Font? title, big, strong, body, small, icons;
    private float scale;
    private int hover = -1;
    private bool rounded, opensUpward = true;
    private Point anchor;
    private DateTime hiddenAt;

    public MeterPanel(Action<Provider> select, Action refresh, Action about, Action support, Action quit, Action troubleshooting)
    {
        this.select = select;
        this.troubleshooting = troubleshooting;
        // Segoe Fluent Icons ships with Windows 11; Windows 10 has the same glyphs in Segoe MDL2 Assets.
        using (var fonts = new InstalledFontCollection())
            iconFamily = fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        buttons =
        [
            ("", "Refresh", refresh),
            ("", "About AI Meter", about),
            ("", "Support AI Meter", support),
            ("", "Quit", quit)
        ];
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "AI Meter";
        clock.Tick += (_, _) => Invalidate();
        Deactivate += (_, _) => Close();
        FormClosing += (_, e) =>
        {
            // Closing only hides the panel, so it opens instantly next time.
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            hiddenAt = DateTime.Now;
            Hide();
        };
        VisibleChanged += (_, _) => clock.Enabled = Visible;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            cp.ExStyle |= 0x00000080;    // WS_EX_TOOLWINDOW: keeps it out of Alt+Tab
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11 rounds the corners and draws the border; Windows 10 gets a painted border.
        var round = 2; // DWMWCP_ROUND
        rounded = Environment.OSVersion.Version.Build >= 22000
            && DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int)) == 0;
    }

    /// Opens the panel next to the tray icon, or closes it if it's open. A click on the
    /// tray icon first deactivates (closes) an open panel, so a click right after that
    /// close is treated as "close" too.
    public void Toggle(Point near)
    {
        if (Visible) { Close(); return; }
        if (DateTime.Now - hiddenAt < TimeSpan.FromMilliseconds(400)) return;
        anchor = near;
        Relayout();
        Place();
        Show();
        Activate();
        SetForegroundWindow(Handle);
    }

    public void SetState(PanelState next)
    {
        state = next;
        if (Visible) Relayout();
    }

    /// Draws the panel for the smoke test's screenshots, without showing a window.
    public Bitmap Preview(PanelState preview, Theme look)
    {
        state = preview;
        Relayout(look);
        // Opaque, like the window itself: ClearType text smears on a transparent bitmap.
        var bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        Render(g);
        return bitmap;
    }

    private void Relayout(Theme? look = null)
    {
        theme = look ?? Theme.Current;
        BackColor = theme.Back;
        if (DeviceDpi / 96f != scale)
        {
            scale = DeviceDpi / 96f;
            foreach (var font in new[] { title, big, strong, body, small, icons }) font?.Dispose();
            Font Make(string family, float px, FontStyle style = FontStyle.Regular) => new(family, px * scale, style, GraphicsUnit.Pixel);
            title = Make("Segoe UI Semibold", 16);
            big = Make("Segoe UI Semibold", 32);
            strong = Make("Segoe UI Semibold", 13);
            body = Make("Segoe UI", 13);
            small = Make("Segoe UI", 12);
            icons = Make(iconFamily, 16);
        }
        var bottom = Bottom;
        Size = new Size(S(PanelWidth), Draw(null));
        // Keep the edge next to the taskbar in place when the content grows or shrinks.
        if (Visible && opensUpward) Top = bottom - Height;
        Invalidate();
    }

    /// Puts the panel above (or beside) the taskbar, as close to the click as fits on screen.
    private void Place()
    {
        var screen = Screen.FromPoint(anchor);
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;
        var gap = S(12);
        var x = Math.Max(area.Left + gap, Math.Min(anchor.X - Width / 2, area.Right - Width - gap));
        var y = Math.Max(area.Top + gap, Math.Min(anchor.Y - Height / 2, area.Bottom - Height - gap));
        opensUpward = true;
        if (area.Bottom < bounds.Bottom) y = area.Bottom - Height - gap;
        else if (area.Top > bounds.Top) { y = area.Top + gap; opensUpward = false; }
        else if (area.Left > bounds.Left) x = area.Left + gap;
        else if (area.Right < bounds.Right) x = area.Right - Width - gap;
        Location = new Point(x, y);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
        Place();
    }

    protected override void OnPaint(PaintEventArgs e) => Render(e.Graphics);

    private void Render(Graphics g)
    {
        g.Clear(theme.Back);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Draw(g);
        if (!rounded)
        {
            using var pen = new Pen(theme.Line);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private int S(float value) => (int)Math.Round(value * scale);

    /// Lays out (and, given a Graphics, paints) the panel; returns its height.
    private int Draw(Graphics? g)
    {
        targets.Clear();
        var pad = S(16);
        var inner = S(PanelWidth) - pad * 2;
        var y = pad;

        // Provider switch, like the macOS segmented control.
        var track = new Rectangle(pad, y, inner, S(30));
        Fill(g, track, theme.Track, S(8));
        var providers = (Provider[])Enum.GetValues(typeof(Provider));
        var segment = (track.Width - S(4)) / providers.Length;
        for (var i = 0; i < providers.Length; i++)
        {
            var provider = providers[i];
            var rect = new Rectangle(track.X + S(2) + i * segment, track.Y + S(2), segment, track.Height - S(4));
            var selected = provider == state.Provider;
            var index = Target(rect, "", () => select(provider));
            if (selected) Fill(g, rect, theme.Selected, S(6));
            else if (hover == index) Fill(g, rect, theme.Hover, S(6));
            Write(g, provider.ToString(), selected ? strong! : body!, rect, theme.Text, NoPad | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        y = track.Bottom + S(14);

        if (state.Reading is { } reading)
        {
            // "59% remaining", with both parts sharing a baseline.
            var number = $"{reading.Remaining}%";
            var size = TextRenderer.MeasureText(number, big, Size.Empty, NoPad);
            Write(g, number, big!, new Rectangle(pad, y, size.Width, size.Height), theme.Text, NoPad);
            var label = TextRenderer.MeasureText("remaining", body, Size.Empty, NoPad);
            var labelY = y + (int)(Ascent(big!) - Ascent(body!));
            Write(g, "remaining", body!, new Rectangle(pad + size.Width + S(6), labelY, label.Width, label.Height), theme.Secondary, NoPad);
            y += size.Height + S(8);

            var bar = new Rectangle(pad, y, inner, S(6));
            Fill(g, bar, theme.Track, bar.Height / 2);
            if (reading.Remaining > 0)
                Fill(g, bar with { Width = Math.Max(bar.Height, bar.Width * Math.Min(reading.Remaining, 100) / 100) }, MeterIcon.ColorFor(reading.Remaining), bar.Height / 2);
            y = bar.Bottom + S(14);

            if (reading.Reset is { } reset)
            {
                var (inText, atText) = Usage.ResetText(reset, DateTime.Now);
                y += Line(g, inText, strong!, theme.Text, pad, y, inner) + S(2);
                y += Line(g, char.ToUpper(atText[0]) + atText.Substring(1), small!, theme.Secondary, pad, y, inner);
            }
            else y += Line(g, "Reset time not reported", small!, theme.Secondary, pad, y, inner);
        }
        else
        {
            y += Line(g, "AI usage unavailable", title!, theme.Text, pad, y, inner) + S(2);
            y += Line(g, "Reset time shows once usage can be read.", small!, theme.Secondary, pad, y, inner);
        }

        if (state.Problem is { } problem) y += S(10) + Line(g, problem, small!, theme.Warning, pad, y + S(10), inner);
        if (state.SignIn)
        {
            // Links to the troubleshooting guide, like the macOS menu's link.
            const string text = "Open the troubleshooting guide ›";
            var size = TextRenderer.MeasureText(text, strong, Size.Empty, NoPad);
            var link = new Rectangle(pad - S(6), y + S(6), size.Width + S(12), size.Height + S(8));
            if (hover == Target(link, "", troubleshooting)) Fill(g, link, theme.Hover, S(6));
            Write(g, text, strong!, link, theme.Text, NoPad | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            y = link.Bottom;
        }

        y += S(14);
        if (g is not null) { using var pen = new Pen(theme.Line, Math.Max(1, scale)); g.DrawLine(pen, pad, y, pad + inner, y); }
        y += S(10);

        // Icon buttons, centered, like the macOS window's Refresh / About / Quit row.
        var buttonSize = new Size(S(44), S(34));
        var spacing = S(10);
        var x = pad + (inner - (buttons.Length * buttonSize.Width + (buttons.Length - 1) * spacing)) / 2;
        foreach (var (glyph, tipText, click) in buttons)
        {
            var rect = new Rectangle(new Point(x, y), buttonSize);
            if (hover == Target(rect, tipText, click)) Fill(g, rect, theme.Hover, S(6));
            Write(g, glyph, icons!, rect, theme.Text, NoPad | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += buttonSize.Width + spacing;
        }
        return y + buttonSize.Height + S(10);
    }

    private int Target(Rectangle rect, string tipText, Action click)
    {
        targets.Add((rect, tipText, click));
        return targets.Count - 1;
    }

    private static float Ascent(Font font) =>
        font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);

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

    private static void Fill(Graphics? g, Rectangle rect, Color color, int radius)
    {
        if (g is null) return;
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = targets.FindIndex(t => t.Rect.Contains(e.Location));
        if (index == hover) return;
        hover = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        tip.Hide(this);
        if (index >= 0 && targets[index].Tip is { Length: > 0 } text)
            tip.Show(text, this, targets[index].Rect.X, targets[index].Rect.Bottom + S(4), 2500);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = -1;
        tip.Hide(this);
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
            clock.Dispose();
            tip.Dispose();
            foreach (var font in new[] { title, big, strong, body, small, icons }) font?.Dispose();
        }
        base.Dispose(disposing);
    }
}
