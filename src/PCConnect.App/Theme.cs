using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace PCConnect.App;

internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(13, 16, 23);
    public static readonly Color Sidebar = Color.FromArgb(17, 21, 29);
    public static readonly Color Card = Color.FromArgb(22, 27, 37);
    public static readonly Color Field = Color.FromArgb(31, 37, 49);
    public static readonly Color Line = Color.FromArgb(44, 51, 65);
    public static readonly Color Ink = Color.FromArgb(239, 242, 248);
    public static readonly Color Muted = Color.FromArgb(143, 153, 173);
    public static readonly Color Accent = Color.FromArgb(247, 183, 92);
    public static readonly Color Green = Color.FromArgb(104, 218, 177);
    public static Font Font(float size, bool bold = false) => new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
    public static GraphicsPath Round(RectangleF r, float radius = 14)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    public static void Fill(Graphics g, RectangleF rect, Color color, float radius = 14) { using var path = Round(rect, radius); using var brush = new SolidBrush(color); g.FillPath(brush, path); }
    public static void Border(Graphics g, RectangleF rect, Color color, float radius = 14) { using var path = Round(rect, radius); using var pen = new Pen(color); g.DrawPath(pen, path); }
    public static void Text(Graphics g, string text, Rectangle rect, Font font, Color color, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis)
        => TextRenderer.DrawText(g, text, font, rect, color, flags | TextFormatFlags.NoPadding);
}

internal class Surface : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color SurfaceColor { get; set; } = Theme.Card;
    public Surface() { DoubleBuffered = true; BackColor = Theme.Bg; Padding = new Padding(22); }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Theme.Fill(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), SurfaceColor); Theme.Border(e.Graphics, new RectangleF(.5f, .5f, Width - 2, Height - 2), Theme.Line); }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Theme.Bg); }
}

internal sealed class ActionButton : Button
{
    bool hovered;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Navigation { get; set; }
    public ActionButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Font = Theme.Font(10, true); Cursor = Cursors.Hand; Size = new Size(150, 42); UseMnemonic = false; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent?.BackColor ?? Theme.Bg);
        Color background = Primary ? (hovered ? Color.FromArgb(255, 202, 126) : Theme.Accent) : Selected ? Color.FromArgb(49, 42, 33) : hovered ? Color.FromArgb(43, 50, 64) : Navigation ? Theme.Sidebar : Theme.Field;
        Theme.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), background, 9);
        if (!Navigation && !Primary) Theme.Border(g, new RectangleF(.5f, .5f, Width - 2, Height - 2), Theme.Line, 9);
        Color color = !Enabled ? Theme.Muted : Primary ? Theme.Bg : Selected ? Theme.Accent : Theme.Ink;
        Theme.Text(g, Text, new Rectangle(Navigation ? 16 : 8, 0, Width - (Navigation ? 30 : 16), Height), Font, color, (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) Theme.Border(g, new RectangleF(3, 3, Width - 7, Height - 7), Theme.Accent, 7);
    }
}

internal sealed class Switch : CheckBox
{
    public Switch() { AutoSize = false; Size = new Size(270, 36); Font = Theme.Font(10); ForeColor = Theme.Ink; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
        Theme.Fill(g, new RectangleF(0, 8, 38, 21), Checked ? Theme.Accent : Theme.Line, 11);
        using var brush = new SolidBrush(Checked ? Theme.Bg : Theme.Muted); g.FillEllipse(brush, Checked ? 20 : 3, 11, 15, 15);
        Theme.Text(g, Text, new Rectangle(49, 0, Width - 49, Height), Font, ForeColor);
        if (Focused) Theme.Border(g, new RectangleF(0, 1, Width - 2, Height - 3), Theme.Accent, 5);
    }
}

internal sealed class ThinProgress : Control
{
    int value;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Value { get => value; set { this.value = Math.Clamp(value, 0, 100); Invalidate(); } }
    public ThinProgress() { Height = 5; DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(BackColor); Theme.Fill(e.Graphics, new RectangleF(0, 0, Width, Height), Theme.Line, 2); if (value > 0) Theme.Fill(e.Graphics, new RectangleF(0, 0, Width * value / 100f, Height), Theme.Accent, 2); }
}

internal sealed class WorkspaceCanvas : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Connected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Remote { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool SecondaryRight { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsPrimary { get; set; } = true;
    public WorkspaceCanvas() { DoubleBuffered = true; Height = 275; BackColor = Theme.Bg; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), Theme.Card, 16);
        Theme.Border(g, new RectangleF(.5f, .5f, Width - 2, Height - 2), Theme.Line, 16);
        using var dot = new SolidBrush(Color.FromArgb(36, 42, 54));
        for (int x = 22; x < Width - 15; x += 20) for (int y = 21; y < Height - 15; y += 20) g.FillEllipse(dot, x, y, 1.5f, 1.5f);
        using var small = Theme.Font(8, true); using var title = Theme.Font(15, true); using var label = Theme.Font(9);
        Theme.Text(g, "YOUR SCREEN ARRANGEMENT", new Rectangle(24, 15, 300, 22), small, Theme.Muted);
        Theme.Text(g, Connected ? "●  LIVE CONNECTION" : "○  READY TO PAIR", new Rectangle(Width - 170, 15, 145, 22), small, Connected ? Theme.Green : Theme.Accent, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        int w = Math.Min(265, (Width - 180) / 2), h = 137, y0 = 69;
        int x1 = Width / 2 - w - 57, x2 = Width / 2 + 57;
        using var link = new Pen(Connected ? Theme.Accent : Theme.Line, 2) { DashStyle = DashStyle.Dash };
        g.DrawLine(link, x1 + w, y0 + h / 2, x2, y0 + h / 2);
        Theme.Fill(g, new RectangleF(Width / 2 - 23, y0 + h / 2 - 23, 46, 46), Color.FromArgb(49, 42, 33), 23);
        using var linkFont = Theme.Font(19); Theme.Text(g, "↔", new Rectangle(Width / 2 - 23, y0 + h / 2 - 24, 46, 46), linkFont, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        DrawMonitor(x1, y0, w, h, SecondaryRight, SecondaryRight ? !Remote : Remote);
        DrawMonitor(x2, y0, w, h, !SecondaryRight, SecondaryRight ? Remote : !Remote);
        Theme.Text(g, "Move your pointer across the adjoining edge to switch laptops", new Rectangle(20, Height - 35, Width - 40, 22), label, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        void DrawMonitor(int x, int y, int width, int height, bool personal, bool active)
        {
            Theme.Fill(g, new RectangleF(x - 4, y - 4, width + 8, height + 8), Color.FromArgb(11, 14, 20), 12);
            using var outer = Theme.Round(new RectangleF(x, y, width, height), 10);
            using var gradient = new LinearGradientBrush(new Point(x, y), new Point(x + width, y + height), personal ? Color.FromArgb(48, 44, 45) : Color.FromArgb(29, 45, 59), Color.FromArgb(18, 23, 33)); g.FillPath(gradient, outer);
            Theme.Border(g, new RectangleF(x, y, width, height), active && (personal || Connected) ? Theme.Accent : Color.FromArgb(64, 74, 91), 10);
            Theme.Fill(g, new RectangleF(x + 17, y + 17, 26, 22), personal ? Color.FromArgb(69, 52, 33) : Color.FromArgb(37, 60, 74), 5);
            Theme.Text(g, personal ? "01" : "02", new Rectangle(x + 17, y + 17, 26, 22), small, personal ? Theme.Accent : Color.FromArgb(138, 193, 217), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Text(g, personal ? "Personal laptop" : "Office laptop", new Rectangle(x + 18, y + 48, width - 36, 32), title, Theme.Ink);
            Theme.Text(g, personal == IsPrimary ? "THIS DEVICE" : "PAIRED DEVICE", new Rectangle(x + 18, y + 87, width - 36, 20), small, Theme.Muted);
            Theme.Fill(g, new RectangleF(x + 28, y + height + 6, width - 56, 4), Color.FromArgb(65, 72, 85), 2);
            if (active && (personal || Connected)) Theme.Text(g, "↗", new Rectangle(x + width - 38, y + height - 37, 28, 30), title, Theme.Accent);
        }
    }
}

internal sealed class DropCanvas : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Hovering { get; set; }
    public DropCanvas() { Height = 182; DoubleBuffered = true; Cursor = Cursors.Hand; AccessibleName = "Drop files or folders here to send a verified copy"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), Hovering ? Color.FromArgb(46, 40, 32) : Theme.Card, 16);
        using var path = Theme.Round(new RectangleF(1, 1, Width - 3, Height - 3), 16); using var border = new Pen(Hovering ? Theme.Accent : Theme.Line) { DashStyle = DashStyle.Dash }; g.DrawPath(border, path);
        Theme.Fill(g, new RectangleF(Width / 2 - 26, 16, 52, 52), Color.FromArgb(53, 43, 31), 15);
        using var icon = Theme.Font(25); using var title = Theme.Font(18, true); using var sub = Theme.Font(10);
        Theme.Text(g, "↑", new Rectangle(Width / 2 - 25, 12, 50, 54), icon, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Text(g, "A little drag. A big handoff.", new Rectangle(10, 79, Width - 20, 38), title, Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Text(g, "Drop files and folders here, or click to browse.", new Rectangle(10, 120, Width - 20, 24), sub, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Theme.Text(g, "Encrypted in transit  ·  Verified on arrival  ·  Originals stay here", new Rectangle(10, 148, Width - 20, 22), sub, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}



internal sealed class ChoiceControl : Control
{
    readonly ContextMenuStrip menu;
    int selected = -1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedIndexChanged;

    public ChoiceControl(params string[] options)
    {
        DoubleBuffered = true;
        Font = Theme.Font(10);
        BackColor = Theme.Field;
        ForeColor = Theme.Ink;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.ComboBox;
        menu = new ContextMenuStrip { BackColor = Theme.Field, ForeColor = Theme.Ink, ShowImageMargin = false };
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            menu.Items.Add(options[i], null, (_, _) => SelectedIndex = index);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var label = selected < 0 ? "Choose…" : menu.Items[selected].Text ?? "";
        Theme.Text(e.Graphics, label, new Rectangle(0, 0, Width - 24, Height), Font, Enabled ? ForeColor : Theme.Muted);
        Theme.Text(e.Graphics, "⌄", new Rectangle(Width - 22, 0, 22, Height), Font, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        menu.Font = Font;
        menu.Show(this, new Point(0, Height + 8));
    }

    protected override void Dispose(bool disposing)
    {
        // WinForms still uses the popup after it raises Closed during item activation.
        if (disposing) menu.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Space:
            case Keys.Enter:
                OnClick(EventArgs.Empty);
                break;
            case Keys.Left:
            case Keys.Up:
                SelectedIndex = Math.Max(0, selected - 1);
                break;
            case Keys.Right:
            case Keys.Down:
                SelectedIndex = Math.Min(menu.Items.Count - 1, selected + 1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
}
