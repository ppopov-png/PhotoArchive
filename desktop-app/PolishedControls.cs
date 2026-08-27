using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PhotoArchive;

internal enum UiIcon
{
    None,
    Refresh,
    Gallery,
    CheckSquare,
    Calendar,
    Filter,
    Help,
    Edit,
    Folder,
    Smartphone
}

internal sealed class UiButton : Control
{
    bool hover;
    bool pressed;

    public int Radius { get; set; } = 12;
    public int BorderWidth { get; set; } = 1;
    public Color BorderColor { get; set; } = AppTheme.BorderStrong;
    public Color HoverBackColor { get; set; } = AppTheme.SurfaceSoft;
    public Color PressedBackColor { get; set; } = AppTheme.PrimarySoft;
    public UiIcon Icon { get; set; }
    public int IconSize { get; set; } = 17;
    public int IconGap { get; set; } = 9;

    public UiButton()
    {
        Size = new Size(150, 44);
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.TextPrimary;
        Font = AppTheme.ButtonFont(9.5f);
        Cursor = Cursors.Hand;
        TabStop = false;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
    }

    public void PerformClick()
    {
        if (Enabled) OnClick(EventArgs.Empty);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled) pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var fill = !Enabled
            ? Color.FromArgb(245, 246, 250)
            : pressed ? PressedBackColor
            : hover ? HoverBackColor
            : BackColor;

        using var path = UiGeometry.Rounded(rect, Radius);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);

        if (BorderWidth > 0)
        {
            using var border = new Pen(Enabled ? BorderColor : AppTheme.Border, BorderWidth);
            e.Graphics.DrawPath(border, path);
        }

        var textColor = Enabled ? ForeColor : AppTheme.TextMuted;
        var hasIcon = Icon != UiIcon.None;
        using var textFont = new Font(Font.FontFamily, Font.Size, Font.Style, GraphicsUnit.Point);
        var measured = TextRenderer.MeasureText(Text ?? string.Empty, textFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        var contentWidth = measured.Width + (hasIcon ? IconSize + IconGap : 0);
        var startX = Math.Max(10, (Width - contentWidth) / 2);

        if (hasIcon)
        {
            var iconRect = new Rectangle(startX, (Height - IconSize) / 2, IconSize, IconSize);
            VectorGlyph.Draw(e.Graphics, Icon, iconRect, textColor, 1.7f);
            startX += IconSize + IconGap;
        }

        var textRect = new Rectangle(startX, 0, Math.Max(1, Width - startX - 10), Height);
        TextRenderer.DrawText(e.Graphics, Text ?? string.Empty, textFont, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}

internal sealed class VectorIconView : Control
{
    public UiIcon Icon { get; set; } = UiIcon.None;
    public Color IconColor { get; set; } = AppTheme.Primary;
    public int IconPadding { get; set; } = 12;

    public VectorIconView()
    {
        Size = new Size(48, 48);
        BackColor = AppTheme.PrimarySoft;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var shape = UiGeometry.Rounded(rect, 14);
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillPath(background, shape);
        var iconRect = Rectangle.Inflate(rect, -IconPadding, -IconPadding);
        VectorGlyph.Draw(e.Graphics, Icon, iconRect, IconColor, 1.8f);
    }
}

internal static class VectorGlyph
{
    public static void Draw(Graphics g, UiIcon icon, Rectangle r, Color color, float width)
    {
        if (icon == UiIcon.None || r.Width <= 2 || r.Height <= 2) return;
        using var pen = new Pen(color, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (icon)
        {
            case UiIcon.Refresh:
                DrawRefresh(g, pen, r);
                break;
            case UiIcon.Gallery:
                DrawGallery(g, pen, r);
                break;
            case UiIcon.CheckSquare:
                DrawCheckSquare(g, pen, r);
                break;
            case UiIcon.Calendar:
                DrawCalendar(g, pen, r);
                break;
            case UiIcon.Filter:
                DrawFilter(g, pen, r);
                break;
            case UiIcon.Help:
                DrawHelp(g, pen, r);
                break;
            case UiIcon.Edit:
                DrawEdit(g, pen, r);
                break;
            case UiIcon.Folder:
                DrawFolder(g, pen, r);
                break;
            case UiIcon.Smartphone:
                DrawPhone(g, pen, r);
                break;
        }
    }

    static void DrawRefresh(Graphics g, Pen pen, Rectangle r)
    {
        var arc = Rectangle.Inflate(r, -2, -2);
        g.DrawArc(pen, arc, 38, 285);
        var p = PointOnEllipse(arc, 38);
        g.DrawLine(pen, p.X, p.Y, p.X - 1, p.Y - 6);
        g.DrawLine(pen, p.X, p.Y, p.X + 5, p.Y - 3);
    }

    static void DrawGallery(Graphics g, Pen pen, Rectangle r)
    {
        var box = Rectangle.Inflate(r, -1, -2);
        g.DrawRoundedRectangle(pen, box, 4);
        g.DrawEllipse(pen, box.X + 4, box.Y + 4, 3, 3);
        g.DrawLines(pen, new[]
        {
            new Point(box.X + 3, box.Bottom - 4),
            new Point(box.X + box.Width / 3, box.Y + box.Height / 2),
            new Point(box.X + box.Width / 2, box.Bottom - 6),
            new Point(box.Right - 3, box.Y + box.Height / 3)
        });
    }

    static void DrawCheckSquare(Graphics g, Pen pen, Rectangle r)
    {
        var box = Rectangle.Inflate(r, -2, -2);
        g.DrawRoundedRectangle(pen, box, 4);
        g.DrawLines(pen, new[]
        {
            new Point(box.X + 4, box.Y + box.Height / 2),
            new Point(box.X + box.Width / 2 - 1, box.Bottom - 5),
            new Point(box.Right - 4, box.Y + 5)
        });
    }

    static void DrawCalendar(Graphics g, Pen pen, Rectangle r)
    {
        var box = Rectangle.Inflate(r, -2, -2);
        g.DrawRoundedRectangle(pen, box, 4);
        g.DrawLine(pen, box.X, box.Y + 5, box.Right, box.Y + 5);
        g.DrawLine(pen, box.X + 4, box.Y - 1, box.X + 4, box.Y + 4);
        g.DrawLine(pen, box.Right - 4, box.Y - 1, box.Right - 4, box.Y + 4);
    }

    static void DrawFilter(Graphics g, Pen pen, Rectangle r)
    {
        var left = r.X + 2;
        var right = r.Right - 2;
        var top = r.Y + 2;
        var mid = r.Y + r.Height / 2;
        g.DrawLine(pen, left, top, right, top);
        g.DrawLine(pen, left + 3, mid, right - 3, mid);
        g.DrawLine(pen, left + 6, r.Bottom - 3, right - 6, r.Bottom - 3);
        g.DrawEllipse(pen, left + 4, top - 2, 4, 4);
        g.DrawEllipse(pen, right - 8, mid - 2, 4, 4);
        g.DrawEllipse(pen, left + 7, r.Bottom - 5, 4, 4);
    }

    static void DrawHelp(Graphics g, Pen pen, Rectangle r)
    {
        var box = Rectangle.Inflate(r, -1, -1);
        g.DrawEllipse(pen, box);
        using var font = new Font("Segoe UI", Math.Max(7f, r.Height * .58f), FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "?", font, box, pen.Color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    static void DrawEdit(Graphics g, Pen pen, Rectangle r)
    {
        var p1 = new Point(r.X + 3, r.Bottom - 4);
        var p2 = new Point(r.Right - 4, r.Y + 3);
        g.DrawLine(pen, p1, p2);
        g.DrawLine(pen, p1.X, p1.Y, p1.X + 5, p1.Y - 1);
        g.DrawLine(pen, p2.X - 3, p2.Y - 2, p2.X + 2, p2.Y + 3);
    }

    static void DrawFolder(Graphics g, Pen pen, Rectangle r)
    {
        var x = r.X + 1;
        var y = r.Y + 4;
        var w = r.Width - 2;
        var h = r.Height - 7;
        using var path = new GraphicsPath();
        path.StartFigure();
        path.AddLine(x, y + 3, x + w / 3, y + 3);
        path.AddLine(x + w / 3, y + 3, x + w / 3 + 4, y);
        path.AddLine(x + w / 3 + 4, y, x + w - 1, y);
        path.AddLine(x + w - 1, y, x + w - 1, y + h);
        path.AddLine(x + w - 1, y + h, x, y + h);
        path.CloseFigure();
        g.DrawPath(pen, path);
    }

    static void DrawPhone(Graphics g, Pen pen, Rectangle r)
    {
        var box = Rectangle.Inflate(r, -3, -1);
        g.DrawRoundedRectangle(pen, box, 4);
        g.DrawLine(pen, box.X + box.Width / 3, box.Bottom - 3, box.Right - box.Width / 3, box.Bottom - 3);
    }

    static Point PointOnEllipse(Rectangle r, double degrees)
    {
        var angle = degrees * Math.PI / 180.0;
        var cx = r.X + r.Width / 2.0;
        var cy = r.Y + r.Height / 2.0;
        return new Point(
            (int)Math.Round(cx + Math.Cos(angle) * r.Width / 2.0),
            (int)Math.Round(cy + Math.Sin(angle) * r.Height / 2.0));
    }
}
