using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PhotoArchive;

internal sealed class DeviceSelectorView : Control
{
    bool hover;
    string displayText = "Телефон не подключён";

    public string DisplayText
    {
        get => displayText;
        set { displayText = string.IsNullOrWhiteSpace(value) ? "Телефон не подключён" : value; Invalidate(); }
    }

    public DeviceSelectorView()
    {
        Size = new Size(320, 48);
        Cursor = Cursors.Hand;
        Font = AppTheme.ButtonFont(9.7f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiGeometry.Rounded(rect, 12);
        using var background = new SolidBrush(hover ? AppTheme.SurfaceSoft : AppTheme.Surface);
        using var border = new Pen(hover ? Color.FromArgb(197, 203, 226) : AppTheme.BorderStrong, 1f);
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);

        using var iconPen = new Pen(AppTheme.TextPrimary, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var phone = new Rectangle(17, 13, 13, 21);
        e.Graphics.DrawRoundedRectangle(iconPen, phone, 4);
        e.Graphics.DrawLine(iconPen, phone.X + 4, phone.Bottom - 4, phone.Right - 4, phone.Bottom - 4);

        var textRect = new Rectangle(43, 0, Math.Max(40, Width - 78), Height);
        TextRenderer.DrawText(e.Graphics, displayText, Font, textRect, AppTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using var chevron = new Pen(AppTheme.TextSecondary, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = Width - 22;
        var cy = Height / 2;
        e.Graphics.DrawLine(chevron, cx - 4, cy - 2, cx, cy + 2);
        e.Graphics.DrawLine(chevron, cx, cy + 2, cx + 4, cy - 2);
    }
}

internal sealed class PathDisplayView : Control
{
    bool hover;
    string pathText = "";

    public string PathText
    {
        get => pathText;
        set { pathText = value ?? ""; Invalidate(); }
    }

    public PathDisplayView()
    {
        Height = 44;
        Font = AppTheme.BodyFont(10f);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var shape = UiGeometry.Rounded(rect, 11);
        using var background = new SolidBrush(hover ? Color.FromArgb(253, 253, 255) : AppTheme.Surface);
        using var border = new Pen(hover ? Color.FromArgb(199, 205, 226) : AppTheme.BorderStrong);
        e.Graphics.FillPath(background, shape);
        e.Graphics.DrawPath(border, shape);

        var textRect = new Rectangle(14, 0, Math.Max(20, Width - 28), Height);
        TextRenderer.DrawText(e.Graphics, pathText, Font, textRect, AppTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class MediaGalleryView : ScrollableControl
{
    sealed class GroupBlock
    {
        public required string Title { get; init; }
        public required List<MediaItem> Items { get; init; }
        public int Y { get; set; }
        public int Height { get; set; }
        public int Rows { get; set; }
    }

    readonly List<MediaItem> source = new();
    readonly HashSet<string> selected = new();
    readonly List<GroupBlock> groups = new();
    int columns = 1;
    int cardWidth = 216;
    const int CardHeight = 154;
    const int Gap = 16;
    const int Side = 20;
    const int HeaderHeight = 42;
    const int GroupBottom = 18;
    MediaItem? hovered;

    public Func<string, Image?>? ThumbnailProvider { get; set; }
    public event EventHandler? SelectionChanged;

    public IReadOnlyCollection<string> SelectedPaths => selected;

    public MediaGalleryView()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = AppTheme.Surface;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void SetItems(IEnumerable<MediaItem> items, IEnumerable<string>? selectedPaths = null)
    {
        source.Clear();
        source.AddRange(items);
        if (selectedPaths != null)
        {
            selected.Clear();
            foreach (var path in selectedPaths) selected.Add(path);
        }
        RebuildLayout();
        Invalidate();
    }

    public void SelectAll()
    {
        selected.Clear();
        foreach (var item in source) selected.Add(item.Remote);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void ClearSelection()
    {
        selected.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void RefreshThumbnail(string remote)
    {
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildLayout();
    }

    void RebuildLayout()
    {
        groups.Clear();
        var available = Math.Max(240, ClientSize.Width - Side * 2 - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        columns = Math.Max(1, (available + Gap) / (216 + Gap));
        cardWidth = Math.Max(174, (available - (columns - 1) * Gap) / columns);

        var y = 14;
        foreach (var group in source.GroupBy(x => x.Group))
        {
            var list = group.ToList();
            var rows = (int)Math.Ceiling(list.Count / (double)columns);
            var height = HeaderHeight + rows * (CardHeight + Gap) + GroupBottom;
            groups.Add(new GroupBlock { Title = group.Key, Items = list, Y = y, Rows = rows, Height = height });
            y += height;
        }
        AutoScrollMinSize = new Size(0, Math.Max(ClientSize.Height, y + 10));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (source.Count == 0)
        {
            DrawEmptyState(e.Graphics);
            return;
        }

        var scrollY = -AutoScrollPosition.Y;
        var visibleTop = scrollY;
        var visibleBottom = scrollY + ClientSize.Height;

        foreach (var group in groups)
        {
            if (group.Y > visibleBottom || group.Y + group.Height < visibleTop) continue;
            DrawGroup(e.Graphics, group, scrollY);
        }
    }

    void DrawEmptyState(Graphics g)
    {
        var center = new Point(ClientSize.Width / 2, ClientSize.Height / 2 - 24);
        using var halo = new SolidBrush(AppTheme.PrimarySoft);
        g.FillEllipse(halo, center.X - 34, center.Y - 56, 68, 68);
        using var iconPen = new Pen(AppTheme.Primary, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var picture = new Rectangle(center.X - 15, center.Y - 39, 30, 24);
        g.DrawRoundedRectangle(iconPen, picture, 6);
        g.DrawEllipse(iconPen, picture.X + 6, picture.Y + 5, 5, 5);
        g.DrawLines(iconPen, new[]
        {
            new Point(picture.X + 4, picture.Bottom - 5),
            new Point(picture.X + 12, picture.Y + 13),
            new Point(picture.X + 18, picture.Bottom - 8),
            new Point(picture.Right - 4, picture.Y + 11)
        });

        using var titleFont = AppTheme.HeadingFont(14.5f);
        using var bodyFont = AppTheme.BodyFont(9.8f);
        var titleRect = new Rectangle(0, center.Y + 22, ClientSize.Width, 30);
        TextRenderer.DrawText(g, "Медиатека пока пуста", titleFont, titleRect, AppTheme.TextPrimary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var bodyRect = new Rectangle(Math.Max(20, center.X - 260), center.Y + 55, Math.Min(520, ClientSize.Width - 40), 50);
        TextRenderer.DrawText(g, "Подключите телефон и нажмите «Медиатека», чтобы увидеть фото и видео по датам.", bodyFont, bodyRect, AppTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
    }

    void DrawGroup(Graphics g, GroupBlock group, int scrollY)
    {
        var headerY = group.Y - scrollY;
        using var headerFont = AppTheme.HeadingFont(10.6f);
        using var countFont = AppTheme.BodyFont(9f);
        TextRenderer.DrawText(g, group.Title, headerFont, new Rectangle(Side, headerY, 420, HeaderHeight), AppTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, $"{group.Items.Count:N0} файлов  ⌃", countFont,
            new Rectangle(Math.Max(Side + 430, ClientSize.Width - 190), headerY, 160, HeaderHeight), AppTheme.TextSecondary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        var startY = group.Y + HeaderHeight;
        var firstVisibleRow = Math.Max(0, (scrollY - startY - CardHeight - Gap) / (CardHeight + Gap));
        var lastVisibleRow = Math.Min(group.Rows - 1, (scrollY + ClientSize.Height - startY) / (CardHeight + Gap) + 1);
        if (lastVisibleRow < firstVisibleRow) return;

        for (var row = firstVisibleRow; row <= lastVisibleRow; row++)
        {
            for (var col = 0; col < columns; col++)
            {
                var index = row * columns + col;
                if (index >= group.Items.Count) break;
                var item = group.Items[index];
                var x = Side + col * (cardWidth + Gap);
                var y = startY + row * (CardHeight + Gap) - scrollY;
                DrawCard(g, item, new Rectangle(x, y, cardWidth, CardHeight));
            }
        }
    }

    void DrawCard(Graphics g, MediaItem item, Rectangle rect)
    {
        var isSelected = selected.Contains(item.Remote);
        var isHover = ReferenceEquals(hovered, item);
        using var cardPath = UiGeometry.Rounded(new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), 12);
        using var cardBrush = new SolidBrush(isSelected ? AppTheme.PrimarySoft : AppTheme.Surface);
        using var cardPen = new Pen(isSelected ? AppTheme.Primary : (isHover ? Color.FromArgb(205, 210, 229) : AppTheme.Border), isSelected ? 1.6f : 1f);
        g.FillPath(cardBrush, cardPath);
        g.DrawPath(cardPen, cardPath);

        var imageRect = new Rectangle(rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
        using var imagePath = UiGeometry.Rounded(imageRect, 10);
        var oldClip = g.Clip;
        g.SetClip(imagePath);
        var image = ThumbnailProvider?.Invoke(item.Remote);
        if (image != null)
        {
            DrawCrop(g, image, imageRect);
        }
        else
        {
            using var background = new SolidBrush(item.Video ? Color.FromArgb(241, 239, 252) : Color.FromArgb(239, 243, 252));
            g.FillRectangle(background, imageRect);
            DrawPlaceholderGlyph(g, imageRect, item.Video);
        }
        g.Clip = oldClip;

        DrawCheckbox(g, imageRect, isSelected);
        DrawBadge(g, imageRect, item);
        if (item.Video) DrawPlay(g, imageRect);
    }

    static void DrawCrop(Graphics g, Image image, Rectangle target)
    {
        var scale = Math.Max(target.Width / (double)image.Width, target.Height / (double)image.Height);
        var width = (int)Math.Ceiling(image.Width * scale);
        var height = (int)Math.Ceiling(image.Height * scale);
        var x = target.X + (target.Width - width) / 2;
        var y = target.Y + (target.Height - height) / 2;
        g.DrawImage(image, new Rectangle(x, y, width, height));
    }

    static void DrawPlaceholderGlyph(Graphics g, Rectangle imageRect, bool video)
    {
        var center = new Point(imageRect.X + imageRect.Width / 2, imageRect.Y + imageRect.Height / 2);
        using var pen = new Pen(video ? Color.FromArgb(104, 85, 215) : AppTheme.Primary, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (video)
        {
            g.DrawEllipse(pen, center.X - 22, center.Y - 22, 44, 44);
            g.DrawPolygon(pen, new[]
            {
                new Point(center.X - 6, center.Y - 10),
                new Point(center.X - 6, center.Y + 10),
                new Point(center.X + 11, center.Y)
            });
        }
        else
        {
            var rect = new Rectangle(center.X - 22, center.Y - 17, 44, 34);
            g.DrawRoundedRectangle(pen, rect, 8);
            g.DrawEllipse(pen, rect.X + 8, rect.Y + 7, 6, 6);
            g.DrawLines(pen, new[]
            {
                new Point(rect.X + 6, rect.Bottom - 7),
                new Point(rect.X + 17, rect.Y + 17),
                new Point(rect.X + 24, rect.Bottom - 10),
                new Point(rect.Right - 6, rect.Y + 15)
            });
        }
    }

    static void DrawCheckbox(Graphics g, Rectangle imageRect, bool isSelected)
    {
        var rect = new Rectangle(imageRect.X + 10, imageRect.Y + 10, 21, 21);
        using var shape = UiGeometry.Rounded(rect, 6);
        using var brush = new SolidBrush(isSelected ? AppTheme.Primary : Color.FromArgb(238, 255, 255, 255));
        using var pen = new Pen(isSelected ? AppTheme.Primary : Color.FromArgb(166, 174, 189), 1.1f);
        g.FillPath(brush, shape);
        g.DrawPath(pen, shape);
        if (!isSelected) return;
        using var tick = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(tick, new[]
        {
            new Point(rect.X + 5, rect.Y + 11),
            new Point(rect.X + 9, rect.Y + 15),
            new Point(rect.X + 16, rect.Y + 7)
        });
    }

    static void DrawBadge(Graphics g, Rectangle imageRect, MediaItem item)
    {
        var text = item.Video ? "MP4" : System.IO.Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text)) text = item.Video ? "VIDEO" : "PHOTO";
        using var font = AppTheme.CaptionFont(7.8f);
        var size = TextRenderer.MeasureText(text, font);
        var rect = new Rectangle(imageRect.Right - size.Width - 17, imageRect.Bottom - 26, size.Width + 10, 19);
        using var shape = UiGeometry.Rounded(rect, 7);
        using var brush = new SolidBrush(Color.FromArgb(176, 28, 31, 40));
        g.FillPath(brush, shape);
        TextRenderer.DrawText(g, text, font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    static void DrawPlay(Graphics g, Rectangle imageRect)
    {
        var center = new Point(imageRect.X + imageRect.Width / 2, imageRect.Y + imageRect.Height / 2);
        using var bg = new SolidBrush(Color.FromArgb(150, 20, 22, 30));
        using var border = new Pen(Color.White, 1.4f);
        g.FillEllipse(bg, center.X - 20, center.Y - 20, 40, 40);
        g.DrawEllipse(border, center.X - 20, center.Y - 20, 40, 40);
        using var play = new SolidBrush(Color.White);
        g.FillPolygon(play, new[]
        {
            new Point(center.X - 5, center.Y - 9),
            new Point(center.X - 5, center.Y + 9),
            new Point(center.X + 10, center.Y)
        });
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (ReferenceEquals(hit, hovered)) return;
        hovered = hit;
        Cursor = hovered == null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovered = null;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var item = HitTest(e.Location);
        if (item == null) return;
        if (!selected.Add(item.Remote)) selected.Remove(item.Remote);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    MediaItem? HitTest(Point clientPoint)
    {
        var contentX = clientPoint.X - AutoScrollPosition.X;
        var contentY = clientPoint.Y - AutoScrollPosition.Y;
        foreach (var group in groups)
        {
            var startY = group.Y + HeaderHeight;
            if (contentY < startY || contentY >= group.Y + group.Height) continue;
            var localY = contentY - startY;
            var row = localY / (CardHeight + Gap);
            if (row < 0 || row >= group.Rows) return null;
            var rowOffset = localY % (CardHeight + Gap);
            if (rowOffset >= CardHeight) return null;
            var localX = contentX - Side;
            if (localX < 0) return null;
            var col = localX / (cardWidth + Gap);
            if (col < 0 || col >= columns) return null;
            var colOffset = localX % (cardWidth + Gap);
            if (colOffset >= cardWidth) return null;
            var index = row * columns + col;
            return index >= 0 && index < group.Items.Count ? group.Items[index] : null;
        }
        return null;
    }
}

internal static class UiGeometry
{
    public static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle rectangle, int radius)
    {
        using var path = Rounded(rectangle, radius);
        graphics.DrawPath(pen, path);
    }
}
