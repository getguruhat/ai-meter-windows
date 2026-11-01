using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace AIMeter;

internal sealed record PanelState(Provider Provider, Reading? Reading, string? Problem, bool SignIn = false);

/// Panel colors, following the Windows "app mode" light/dark setting. The same palette as
/// the macOS menu (and ServerHat): a grey page, white cards, vivid accents.
internal sealed record Theme(Color Back, Color Text, Color Secondary, Color Track, Color Selected, Color Hover, Color Line, Color Warning,
    Color Card, Color Stroke, Color Accent, Color Green, Color Red)
{
    public static readonly Theme Light = new(Color.FromArgb(243, 244, 247), Color.FromArgb(26, 28, 35), Color.FromArgb(107, 113, 126),
        Color.FromArgb(226, 227, 231), Color.White, Color.FromArgb(239, 240, 243), Color.FromArgb(226, 227, 231), Color.FromArgb(245, 128, 25),
        Color.White, Color.FromArgb(20, 0, 0, 0), Color.FromArgb(67, 98, 255), Color.FromArgb(22, 173, 94), Color.FromArgb(225, 58, 58));
    public static readonly Theme Dark = new(Color.FromArgb(20, 20, 22), Color.FromArgb(245, 246, 248), Color.FromArgb(157, 162, 173),
        Color.FromArgb(37, 37, 40), Color.FromArgb(52, 53, 58), Color.FromArgb(46, 47, 51), Color.FromArgb(46, 47, 51), Color.FromArgb(255, 157, 67),
        Color.FromArgb(30, 31, 34), Color.FromArgb(23, 255, 255, 255), Color.FromArgb(82, 111, 255), Color.FromArgb(67, 207, 126), Color.FromArgb(239, 85, 85));

    /// Bar color for what's left: green, orange below 50%, red below 20%.
    public Color Level(int remaining) => remaining >= 50 ? Green : remaining >= 20 ? Warning : Red;

    public static Theme Current
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is 0 ? Dark : Light;
        }
    }
}

/// The window that opens from the tray icon, laid out like the macOS menu: the Codex/Claude
/// switch and round icon buttons on one row, then one card with each usage window (what's
/// left, a colored bar, when it resets), then any problem.
/// Everything is painted by <see cref="Draw"/>, which also measures the layout when
/// called without a Graphics.
internal sealed class MeterPanel : Form
{
    private const int PanelWidth = 300;
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
    private Font? segment, percent, body, note, small, tag, icons;
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
        var text = Strings.Current;
        buttons =
        [
            ("", text.Refresh, refresh),
            ("", text.About, about),
            ("", text.Support, support),
            ("", text.Quit, quit)
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
            foreach (var font in new[] { segment, percent, body, note, small, tag, icons }) font?.Dispose();
            Font Make(string family, float px, FontStyle style = FontStyle.Regular) => new(family, px * scale, style, GraphicsUnit.Pixel);
            segment = Make("Segoe UI Semibold", 12);
            percent = Make("Segoe UI Semibold", 15);
            body = Make("Segoe UI", 13);
            note = Make("Segoe UI", 12);
            small = Make("Segoe UI", 11);
            tag = Make("Segoe UI Semibold", 10);
            icons = Make(iconFamily, 13);
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
        var pad = S(12);
        var inner = S(PanelWidth) - pad * 2;
        var y = pad;

        // Top row: the provider switch, then the icon buttons as circles.
        var button = S(30);
        var gap = S(6);
        var buttonsWidth = buttons.Length * button + (buttons.Length - 1) * gap;
        var track = new Rectangle(pad, y, inner - buttonsWidth - gap, button);
        Fill(g, track, theme.Track, track.Height / 2);
        var providers = (Provider[])Enum.GetValues(typeof(Provider));
        var inset = S(3);
        var slot = (track.Width - inset * 2) / providers.Length;
        for (var i = 0; i < providers.Length; i++)
        {
            var provider = providers[i];
            var rect = new Rectangle(track.X + inset + i * slot, track.Y + inset, slot, track.Height - inset * 2);
            var selected = provider == state.Provider;
            var index = Target(rect, "", () => select(provider));
            if (selected)
            {
                // A soft white pill with a faint shadow, quiet next to the colored bars.
                Fill(g, rect with { Y = rect.Y + Math.Max(1, S(1)) }, Color.FromArgb(28, 0, 0, 0), rect.Height / 2);
                Fill(g, rect, theme.Selected, rect.Height / 2);
            }
            else if (hover == index) Fill(g, rect, theme.Hover, rect.Height / 2);
            Write(g, provider.ToString(), segment!, rect, selected ? theme.Text : theme.Secondary, NoPad | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        var x = track.Right + gap;
        foreach (var (glyph, tipText, click) in buttons)
        {
            var rect = new Rectangle(x, y, button, button);
            Bordered(g, rect, hover == Target(rect, tipText, click) ? theme.Hover : theme.Card, button / 2);
            Write(g, glyph, icons!, rect, theme.Text, NoPad | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            x += button + gap;
        }
        y = track.Bottom + S(10);

        // One card for every usage window (or for "unavailable"), measured first so its
        // background can be painted under the text.
        var cardPad = S(12);
        var radius = S(16);
        var cardHeight = CardContent(null, pad + cardPad, 0, inner - cardPad * 2) + cardPad * 2;
        Bordered(g, new Rectangle(pad, y, inner, cardHeight), theme.Card, radius);
        CardContent(g, pad + cardPad, y + cardPad, inner - cardPad * 2);
        y += cardHeight;

        if (state.Problem is { } problem)
        {
            var problemHeight = Problem(null, pad + cardPad, 0, inner - cardPad * 2) + cardPad * 2;
            y += S(10);
            Bordered(g, new Rectangle(pad, y, inner, problemHeight), theme.Card, radius);
            Problem(g, pad + cardPad, y + cardPad, inner - cardPad * 2);
            y += problemHeight;
        }
        return y + pad;
    }

    /// The usage windows inside the card, separated by a line; returns their height.
    private int CardContent(Graphics? g, int x, int y, int width)
    {
        var text = Strings.Current;
        if (state.Reading is not { } reading)
        {
            var top = y;
            y += Line(g, text.Unavailable, percent!, theme.Text, x, y, width) + S(4);
            y += Line(g, text.UnavailableHint, small!, theme.Secondary, x, y, width);
            return y - top;
        }
        var start = y;
        y += Meter(g, reading.Primary, reading.Plan, x, y, width);
        if (reading.Secondary is { } secondary)
        {
            y += S(12);
            if (g is not null) { using var pen = new Pen(theme.Line, Math.Max(1, scale)); g.DrawLine(pen, x, y, x + width, y); }
            y += S(12);
            y += Meter(g, secondary, null, x, y, width);
        }
        return y - start;
    }

    /// One usage window: its name (with the plan beside the first one), what's left, a bar
    /// colored by how much is left, and when it resets. Returns its height.
    private int Meter(Graphics? g, UsageWindow window, string? plan, int x, int y, int width)
    {
        var text = Strings.Current;
        var start = y;
        // Named by its length: Codex's free plan has a single monthly window.
        var name = window.Seconds < 86_400 ? text.FiveHour : window.Seconds < 8 * 86_400 ? text.Weekly : text.Monthly;
        var left = string.Format(text.PercentLeft, window.Remaining);
        var leftSize = TextRenderer.MeasureText(left, percent, Size.Empty, NoPad);
        var nameSize = TextRenderer.MeasureText(name, body, Size.Empty, NoPad);
        var row = Math.Max(leftSize.Height, nameSize.Height);
        Write(g, name, body!, new Rectangle(x, y + (row - nameSize.Height) / 2, nameSize.Width, nameSize.Height), theme.Secondary, NoPad);
        Write(g, left, percent!, new Rectangle(x + width - leftSize.Width, y + (row - leftSize.Height) / 2, leftSize.Width, leftSize.Height), theme.Text, NoPad);
        if (plan is not null)
        {
            var label = string.Format(text.Plan, plan);
            var labelSize = TextRenderer.MeasureText(label, tag, Size.Empty, NoPad);
            var pill = new Rectangle(x + nameSize.Width + S(6), y + (row - labelSize.Height - S(4)) / 2, labelSize.Width + S(14), labelSize.Height + S(4));
            // Only when it fits between the name and the percentage.
            if (pill.Right + S(6) <= x + width - leftSize.Width)
            {
                Fill(g, pill, Color.FromArgb(31, theme.Accent), pill.Height / 2);
                Write(g, label, tag!, pill, theme.Accent, NoPad | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        y += row + S(6);

        var bar = new Rectangle(x, y, width, S(7));
        Fill(g, bar, theme.Hover, bar.Height / 2);
        if (window.Remaining > 0)
            Fill(g, bar with { Width = Math.Max(bar.Height, bar.Width * Math.Min(window.Remaining, 100) / 100) }, theme.Level(window.Remaining), bar.Height / 2);
        y = bar.Bottom + S(6);

        if (window.Reset is { } reset)
        {
            var (inText, atText) = Usage.ResetText(reset, DateTime.Now);
            y += Line(g, $"{inText} · {atText}", small!, theme.Secondary, x, y, width);
        }
        else y += Line(g, text.ResetNotReported, small!, theme.Secondary, x, y, width);
        return y - start;
    }

    /// A problem with a warning sign, and the troubleshooting link for sign-in problems.
    private int Problem(Graphics? g, int x, int y, int width)
    {
        var text = Strings.Current;
        var start = y;
        var sign = S(18);
        Write(g, "", icons!, new Rectangle(x, y, sign, sign), theme.Warning, NoPad | TextFormatFlags.HorizontalCenter);
        y += Math.Max(sign, Line(g, state.Problem ?? "", note!, theme.Text, x + sign + S(8), y, width - sign - S(8)));
        if (state.SignIn)
        {
            // Wraps when a translation is longer than the card is wide.
            var flags = NoPad | TextFormatFlags.WordBreak;
            var size = TextRenderer.MeasureText(text.Troubleshooting, segment, new Size(width, int.MaxValue), flags);
            var link = new Rectangle(x - S(6), y + S(4), size.Width + S(12), size.Height + S(8));
            if (hover == Target(link, "", troubleshooting)) Fill(g, link, theme.Hover, S(6));
            Write(g, text.Troubleshooting, segment!, new Rectangle(link.X + S(6), link.Y + S(4), size.Width, size.Height), theme.Accent, flags);
            y = link.Bottom;
        }
        return y - start;
    }

    private int Target(Rectangle rect, string tipText, Action click)
    {
        targets.Add((rect, tipText, click));
        return targets.Count - 1;
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

    /// A filled shape with a hairline border, like the macOS cards and buttons.
    private void Bordered(Graphics? g, Rectangle rect, Color color, int radius)
    {
        if (g is null) return;
        Fill(g, rect, color, radius);
        using var path = Round(new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), radius);
        using var pen = new Pen(theme.Stroke, Math.Max(1, scale));
        g.DrawPath(pen, path);
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
            foreach (var font in new[] { segment, percent, body, note, small, tag, icons }) font?.Dispose();
        }
        base.Dispose(disposing);
    }
}
