using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MediaDevices;

namespace PhotoArchive;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

sealed record MediaItem(string Remote, string Name, bool Video, string Group);

sealed class MainForm : Form
{
    readonly ComboBox devices = new();
    readonly RoundButton refresh = new("Обновить телефон");
    readonly RoundButton mediaButton = new("Медиатека");
    readonly RoundButton allButton = new("Выбрать всё");
    readonly RoundButton sortButton = new("Начать сортировку");
    readonly TextBox destination = new();
    readonly Label status = new();
    readonly Label selected = new();
    readonly Label librarySummary = new();
    readonly Label connectionSummary = new();
    readonly DesignedProgressBar progress = new();
    readonly ListView gallery = new BufferedListView();
    readonly ImageList images = new();
    readonly List<MediaItem> allMedia = new();
    readonly HashSet<string> chosen = new();
    readonly HashSet<string> loadedThumbs = new();
    readonly HashSet<string> loadingThumbs = new();
    readonly HashSet<string> knownMediaPaths = new();
    readonly Dictionary<string, Image> thumbCache = new();
    readonly Dictionary<string, ListViewGroup> uiGroups = new();
    readonly Dictionary<string, ListViewItem> rowByPath = new();
    readonly ConcurrentQueue<string> pendingMediaPaths = new();
    readonly ConcurrentQueue<(string Path, byte[] Bytes)> pendingThumbnails = new();
    readonly System.Windows.Forms.Timer mediaPump = new() { Interval = 20 };

    RoundButton? allFilterButton;
    RoundButton? photoFilterButton;
    RoundButton? videoFilterButton;
    Label? helpLabel;

    int expectedMediaCount;
    int receivedMediaCount;
    string? serial;
    MediaDevice? mtpDevice;
    bool thumbBridgeReady;
    bool thumbnailLoadQueued;
    bool mediaPageLoading;
    bool mediaScanComplete;
    bool receivingMediaInfo;
    ScanProgressDialog? scanDialog;
    int mediaPage;
    const int MediaPageSize = 80;
    string filter = "Все";

    public MainForm()
    {
        Text = "ФотоАрхив — сортировка с телефона";
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1400, 920);
        BackColor = AppTheme.Background;
        Font = AppTheme.BodyFont();
        AutoScaleMode = AutoScaleMode.Dpi;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        Resize += (_, _) => ApplyWindowShape();

        images.ColorDepth = ColorDepth.Depth32Bit;
        images.ImageSize = new Size(218, 138);

        gallery.Dock = DockStyle.Fill;
        gallery.View = View.LargeIcon;
        gallery.LargeImageList = images;
        gallery.BorderStyle = BorderStyle.None;
        gallery.BackColor = AppTheme.Surface;
        gallery.ForeColor = AppTheme.TextPrimary;
        gallery.MultiSelect = true;
        gallery.HideSelection = false;
        gallery.ShowItemToolTips = true;
        gallery.OwnerDraw = true;
        gallery.Font = AppTheme.BodyFont(9.5f);
        gallery.DrawItem += DrawGalleryItem;
        gallery.HandleCreated += (_, _) => SetGallerySpacing();
        gallery.ItemSelectionChanged += (_, e) =>
        {
            if (e.Item.Tag is not MediaItem item) return;
            if (e.IsSelected) chosen.Add(item.Remote); else chosen.Remove(item.Remote);
            UpdateSelected();
            gallery.Invalidate(e.Item.Bounds);
        };
        gallery.MouseWheel += async (_, _) =>
        {
            BeginInvoke((Action)LoadVisibleThumbnails);
            if (IsNearBottom()) await FetchNextMediaPage();
        };

        refresh.Click += async (_, _) => await RefreshDevices();
        mediaButton.Click += async (_, _) => await LoadMedia();
        allButton.Click += (_, _) => SelectAllVisible();
        sortButton.Click += async (_, _) => await StartTransferAsync();
        sortButton.Width = 214;
        sortButton.Height = 50;

        devices.DropDownStyle = ComboBoxStyle.DropDownList;
        devices.FlatStyle = FlatStyle.Flat;
        devices.DrawMode = DrawMode.OwnerDrawFixed;
        devices.ItemHeight = 28;
        devices.BackColor = AppTheme.Surface;
        devices.ForeColor = AppTheme.TextPrimary;
        devices.Font = AppTheme.ButtonFont(9.5f);
        devices.DrawItem += DrawDeviceItem;

        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ФотоАрхив");
        destination.BorderStyle = BorderStyle.None;
        destination.BackColor = AppTheme.Surface;
        destination.ForeColor = AppTheme.TextPrimary;
        destination.Font = AppTheme.BodyFont(10f);

        status.Text = "Телефон не найден — подтвердите USB-отладку";
        status.ForeColor = AppTheme.TextSecondary;
        status.Font = AppTheme.CaptionFont();

        progress.Maximum = 1;
        progress.Value = 0;
        progress.Indeterminate = false;

        Controls.Add(BuildModernLayout());
        mediaPump.Tick += (_, _) => PumpMediaPaths();
        mediaPump.Start();
        Shown += async (_, _) =>
        {
            ApplyWindowShape();
            SetGallerySpacing();
            await RefreshDevices();
        };
    }

    Control BuildModernLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            BackColor = AppTheme.Background,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var header = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Radius = 20,
            DrawBorder = true,
            BorderColor = AppTheme.Border,
            Margin = new Padding(0, 0, 0, 12)
        };

        var logo = new BrandMark { Location = new Point(22, 18), Size = new Size(46, 46) };
        var title = new Label
        {
            Text = "ФотоАрхив",
            AutoSize = false,
            Font = AppTheme.TitleFont(23),
            ForeColor = AppTheme.TextPrimary,
            Location = new Point(80, 13),
            Size = new Size(360, 38)
        };
        var subtitle = new Label
        {
            Text = "Сортировка фото и видео с телефона по датам",
            AutoSize = false,
            Font = AppTheme.BodyFont(10.2f),
            ForeColor = AppTheme.TextSecondary,
            Location = new Point(81, 51),
            Size = new Size(430, 24)
        };

        var closeButton = new ChromeButton(ChromeButtonKind.Close) { Size = new Size(42, 34), Cursor = Cursors.Hand };
        var maximizeButton = new ChromeButton(ChromeButtonKind.Maximize) { Size = new Size(42, 34), Cursor = Cursors.Hand };
        var minimizeButton = new ChromeButton(ChromeButtonKind.Minimize) { Size = new Size(42, 34), Cursor = Cursors.Hand };
        closeButton.Click += (_, _) => Close();
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximizeButton.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;

        var deviceShell = new RoundedPanel
        {
            Location = new Point(22, 88),
            Size = new Size(320, 46),
            Radius = 12,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.BorderStrong,
            DrawBorder = true
        };
        var deviceIcon = new Label
        {
            Text = "▯",
            Font = new Font("Segoe Fluent Icons", 14f),
            ForeColor = AppTheme.TextPrimary,
            Location = new Point(14, 11),
            Size = new Size(22, 24),
            TextAlign = ContentAlignment.MiddleCenter
        };
        devices.Location = new Point(43, 7);
        devices.Size = new Size(264, 32);
        deviceShell.Controls.Add(deviceIcon);
        deviceShell.Controls.Add(devices);

        StylePrimary(refresh, 190);
        refresh.Location = new Point(356, 88);
        refresh.Height = 46;
        refresh.Text = "↻  Обновить телефон";

        StyleSecondary(mediaButton, 166);
        mediaButton.Location = new Point(558, 88);
        mediaButton.Height = 46;
        mediaButton.Text = "▧  Медиатека";

        StyleSecondary(allButton, 166);
        allButton.Location = new Point(736, 88);
        allButton.Height = 46;
        allButton.Text = "☑  Выбрать всё";

        helpLabel = new Label
        {
            Text = "ⓘ  Справка",
            AutoSize = false,
            Size = new Size(110, 34),
            ForeColor = AppTheme.TextSecondary,
            Font = AppTheme.ButtonFont(9.4f),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };

        void PositionHeaderRight()
        {
            closeButton.Location = new Point(header.ClientSize.Width - 51, 7);
            maximizeButton.Location = new Point(header.ClientSize.Width - 93, 7);
            minimizeButton.Location = new Point(header.ClientSize.Width - 135, 7);
            helpLabel.Location = new Point(header.ClientSize.Width - 132, 91);
        }
        header.Resize += (_, _) => PositionHeaderRight();
        PositionHeaderRight();

        foreach (Control dragControl in new Control[] { header, logo, title, subtitle })
            dragControl.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, 0x2, 0);
            };

        header.Controls.AddRange(new Control[]
        {
            logo, title, subtitle, deviceShell, refresh, mediaButton, allButton,
            helpLabel, minimizeButton, maximizeButton, closeButton
        });
        root.Controls.Add(header, 0, 0);

        var mediaShell = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 18,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border,
            DrawBorder = true,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(1)
        };

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 65, BackColor = AppTheme.Surface };
        toolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
        };

        var filters = new FlowLayoutPanel
        {
            Location = new Point(14, 11),
            Size = new Size(690, 44),
            BackColor = AppTheme.Surface,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        allFilterButton = FilterButton("Все файлы", "Все", 112);
        photoFilterButton = FilterButton("Фото", "Фото", 88);
        videoFilterButton = FilterButton("Видео", "Видео", 88);
        var period = SecondaryButton("▣  За всё время  ⌄", 166);
        var filtersButton = SecondaryButton("☷  Фильтры", 126);
        filters.Controls.AddRange(new Control[] { allFilterButton, photoFilterButton, videoFilterButton, period, filtersButton });

        selected.Text = "Выбрано: 0 файлов";
        selected.AutoSize = false;
        selected.Size = new Size(230, 44);
        selected.ForeColor = AppTheme.TextSecondary;
        selected.Font = AppTheme.ButtonFont(9.3f);
        selected.TextAlign = ContentAlignment.MiddleRight;
        selected.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        void PositionSelected() => selected.Location = new Point(toolbar.ClientSize.Width - selected.Width - 18, 10);
        toolbar.Resize += (_, _) => PositionSelected();
        PositionSelected();
        toolbar.Controls.Add(filters);
        toolbar.Controls.Add(selected);

        var galleryHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Padding = new Padding(14, 10, 14, 10)
        };
        galleryHost.Controls.Add(gallery);
        mediaShell.Controls.Add(galleryHost);
        mediaShell.Controls.Add(toolbar);
        toolbar.BringToFront();
        root.Controls.Add(mediaShell, 0, 1);

        var destinationPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            Radius = 18,
            BorderColor = AppTheme.Border,
            DrawBorder = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        var folderBox = new RoundedPanel
        {
            Location = new Point(18, 22),
            Size = new Size(50, 50),
            Radius = 14,
            DrawBorder = false,
            BackColor = AppTheme.PrimarySoft
        };
        var folderIcon = new Label
        {
            Text = "▱",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe Fluent Icons", 24f),
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter
        };
        folderBox.Controls.Add(folderIcon);

        var destinationCaption = new Label
        {
            Text = "Папка назначения",
            AutoSize = true,
            Font = AppTheme.CaptionFont(8.8f),
            ForeColor = AppTheme.TextSecondary,
            Location = new Point(82, 16)
        };

        var pathShell = new RoundedPanel
        {
            Location = new Point(82, 39),
            Size = new Size(640, 40),
            Radius = 11,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.BorderStrong,
            DrawBorder = true
        };
        destination.Location = new Point(12, 10);
        destination.Size = new Size(614, 22);
        destination.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        pathShell.Controls.Add(destination);

        var chooseFolder = SecondaryButton("Изменить", 130);
        chooseFolder.Height = 42;
        chooseFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        chooseFolder.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Куда сохранить фотоархив?", SelectedPath = destination.Text };
            if (d.ShowDialog() == DialogResult.OK) destination.Text = d.SelectedPath;
        };

        StylePrimary(sortButton, 218);
        sortButton.Height = 52;
        sortButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        sortButton.Text = "Начать сортировку";

        void PositionDestination()
        {
            var right = destinationPanel.ClientSize.Width - 18;
            sortButton.Location = new Point(right - sortButton.Width, 21);
            chooseFolder.Location = new Point(sortButton.Left - chooseFolder.Width - 12, 26);
            pathShell.Width = Math.Max(300, chooseFolder.Left - 94);
            destination.Width = Math.Max(270, pathShell.Width - 24);
        }
        destinationPanel.Resize += (_, _) => PositionDestination();
        PositionDestination();
        destinationPanel.Controls.AddRange(new Control[] { folderBox, destinationCaption, pathShell, chooseFolder, sortButton });
        root.Controls.Add(destinationPanel, 0, 2);

        var statusBar = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };
        connectionSummary.Text = "●  Телефон не подключён";
        connectionSummary.ForeColor = AppTheme.TextSecondary;
        connectionSummary.Font = AppTheme.CaptionFont(8.8f);
        connectionSummary.Dock = DockStyle.Left;
        connectionSummary.Width = 420;
        connectionSummary.TextAlign = ContentAlignment.MiddleLeft;

        librarySummary.Text = "Всего файлов на телефоне: —";
        librarySummary.ForeColor = AppTheme.TextSecondary;
        librarySummary.Font = AppTheme.CaptionFont(8.8f);
        librarySummary.Dock = DockStyle.Right;
        librarySummary.Width = 520;
        librarySummary.TextAlign = ContentAlignment.MiddleRight;

        status.Visible = false;
        progress.Visible = false;
        statusBar.Controls.Add(connectionSummary);
        statusBar.Controls.Add(librarySummary);
        root.Controls.Add(statusBar, 0, 3);

        SetFilter("Все", render: false);
        return root;
    }

    static void StylePrimary(RoundButton button, int width)
    {
        button.Width = width;
        button.BackColor = AppTheme.Primary;
        button.ForeColor = Color.White;
        button.BorderWidth = 0;
        button.Radius = 12;
    }

    static void StyleSecondary(RoundButton button, int width)
    {
        button.Width = width;
        button.BackColor = AppTheme.Surface;
        button.ForeColor = AppTheme.TextPrimary;
        button.BorderColor = AppTheme.BorderStrong;
        button.BorderWidth = 1;
        button.Radius = 12;
    }

    RoundButton SecondaryButton(string text, int width)
    {
        var button = new RoundButton(text) { Height = 40 };
        StyleSecondary(button, width);
        return button;
    }

    RoundButton FilterButton(string text, string value, int width)
    {
        var button = new RoundButton(text) { Width = width, Height = 40, Radius = 10 };
        button.Click += (_, _) => SetFilter(value);
        return button;
    }

    void SetFilter(string value, bool render = true)
    {
        filter = value;
        if (allFilterButton != null) SetFilterVisual(allFilterButton, value == "Все");
        if (photoFilterButton != null) SetFilterVisual(photoFilterButton, value == "Фото");
        if (videoFilterButton != null) SetFilterVisual(videoFilterButton, value == "Видео");
        if (render) Render();
    }

    static void SetFilterVisual(RoundButton button, bool active)
    {
        button.BackColor = active ? AppTheme.PrimarySoft : AppTheme.Surface;
        button.ForeColor = active ? AppTheme.Primary : AppTheme.TextSecondary;
        button.BorderColor = active ? Color.FromArgb(213, 217, 255) : Color.Transparent;
        button.BorderWidth = active ? 1 : 0;
        button.Invalidate();
    }

    void DrawDeviceItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        e.DrawBackground();
        var text = devices.Items[e.Index]?.ToString() ?? "Телефон";
        var color = (e.State & DrawItemState.Selected) != 0 ? Color.White : AppTheme.TextPrimary;
        TextRenderer.DrawText(e.Graphics, "▯  " + text, devices.Font, e.Bounds, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }

    void DrawGalleryItem(object? sender, DrawListViewItemEventArgs e)
    {
        if (e.Item.Tag is not MediaItem item) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var bounds = e.Bounds;
        bounds.Inflate(-7, -7);
        if (bounds.Width < 40 || bounds.Height < 40) return;

        var selectedItem = e.Item.Selected;
        using var cardPath = RoundedPath(bounds, 12);
        using var cardBrush = new SolidBrush(selectedItem ? AppTheme.PrimarySoft : AppTheme.Surface);
        g.FillPath(cardBrush, cardPath);
        using var cardPen = new Pen(selectedItem ? AppTheme.Primary : AppTheme.Border, selectedItem ? 1.6f : 1f);
        g.DrawPath(cardPen, cardPath);

        var imageRect = new Rectangle(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, Math.Max(40, bounds.Height - 8));
        using var imagePath = RoundedPath(imageRect, 10);
        var previousClip = g.Clip;
        g.SetClip(imagePath);
        if (e.Item.ImageIndex >= 0 && e.Item.ImageIndex < images.Images.Count)
            g.DrawImage(images.Images[e.Item.ImageIndex], imageRect);
        else
        {
            using var placeholder = new SolidBrush(AppTheme.SurfaceMuted);
            g.FillRectangle(placeholder, imageRect);
        }
        g.Clip = previousClip;

        var check = new Rectangle(imageRect.X + 10, imageRect.Y + 10, 21, 21);
        using var checkPath = RoundedPath(check, 6);
        using var checkBrush = new SolidBrush(selectedItem ? AppTheme.Primary : Color.FromArgb(235, 255, 255, 255));
        g.FillPath(checkBrush, checkPath);
        using var checkPen = new Pen(selectedItem ? AppTheme.Primary : Color.FromArgb(170, 178, 191), 1.2f);
        g.DrawPath(checkPen, checkPath);
        if (selectedItem)
        {
            using var tick = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(tick, new[]
            {
                new Point(check.X + 5, check.Y + 11),
                new Point(check.X + 9, check.Y + 15),
                new Point(check.X + 16, check.Y + 7)
            });
        }

        var badgeText = item.Video ? "MP4" : Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(badgeText)) badgeText = item.Video ? "VIDEO" : "PHOTO";
        var badgeFont = AppTheme.CaptionFont(8f);
        var badgeSize = TextRenderer.MeasureText(badgeText, badgeFont);
        var badge = new Rectangle(imageRect.Right - badgeSize.Width - 17, imageRect.Bottom - 27, badgeSize.Width + 10, 20);
        using var badgePath = RoundedPath(badge, 7);
        using var badgeBrush = new SolidBrush(Color.FromArgb(180, 24, 27, 38));
        g.FillPath(badgeBrush, badgePath);
        TextRenderer.DrawText(g, badgeText, badgeFont, badge, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        badgeFont.Dispose();

        if (item.Video)
        {
            var center = new Point(imageRect.X + imageRect.Width / 2, imageRect.Y + imageRect.Height / 2);
            var circle = new Rectangle(center.X - 20, center.Y - 20, 40, 40);
            using var circleBrush = new SolidBrush(Color.FromArgb(170, 22, 24, 34));
            g.FillEllipse(circleBrush, circle);
            using var circlePen = new Pen(Color.White, 1.5f);
            g.DrawEllipse(circlePen, circle);
            using var playBrush = new SolidBrush(Color.White);
            g.FillPolygon(playBrush, new[]
            {
                new Point(center.X - 5, center.Y - 9),
                new Point(center.X - 5, center.Y + 9),
                new Point(center.X + 10, center.Y)
            });
        }
    }

    void SetGallerySpacing()
    {
        if (!gallery.IsHandleCreated) return;
        const int LVM_FIRST = 0x1000;
        const int LVM_SETICONSPACING = LVM_FIRST + 53;
        var packed = (nint)((164 << 16) | 238);
        SendMessage(gallery.Handle, LVM_SETICONSPACING, 0, packed);
    }

    static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
        path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
        path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);

    void ApplyWindowShape()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            Region = null;
            return;
        }
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
        using var path = RoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 18);
        Region = new Region(path);
    }

    async Task RefreshDevices()
    {
        refresh.Enabled = false;
        status.Text = "Проверяем подключение…";
        try
        {
            var found = await Task.Run(() => MediaDeviceManager.Instance.GetDevices().ToList());
            devices.Items.Clear();
            devices.Items.AddRange(found.Select(x => x.FriendlyName).ToArray());
            mtpDevice = found.FirstOrDefault();
            if (mtpDevice != null) devices.SelectedIndex = 0;

            if (found.Count > 0)
            {
                try
                {
                    var adb = await Adb.Run("devices");
                    serial = adb.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim())
                        .Where(x => Regex.IsMatch(x, @"\s+device$"))
                        .Select(x => Regex.Split(x, @"\s+")[0])
                        .FirstOrDefault();
                }
                catch { serial = null; }
            }
            else serial = null;

            status.Text = found.Count == 0
                ? "Телефон не найден — включите режим передачи файлов"
                : $"Телефон подключён: {devices.SelectedItem}";
        }
        catch (Exception ex)
        {
            mtpDevice = null;
            serial = null;
            status.Text = "Ошибка подключения: " + ex.Message;
            Log("Device refresh", ex);
        }
        finally
        {
            refresh.Enabled = true;
            connectionSummary.Text = mtpDevice == null
                ? "●  Телефон не подключён"
                : $"●  Телефон подключён  ·  {devices.SelectedItem}";
            connectionSummary.ForeColor = mtpDevice == null ? AppTheme.TextSecondary : AppTheme.Success;
        }
    }

    async Task LoadMedia()
    {
        if (mtpDevice == null)
        {
            MessageBox.Show(this, "Сначала подключите телефон по USB в режиме передачи файлов.", "ФотоАрхив", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        mediaButton.Enabled = false;
        allButton.Enabled = false;
        gallery.Enabled = false;
        receivingMediaInfo = true;
        progress.Indeterminate = false;
        progress.Maximum = 1;
        progress.Value = 0;
        scanDialog = new ScanProgressDialog(this);
        scanDialog.Show(this);

        try
        {
            allMedia.Clear();
            chosen.Clear();
            loadedThumbs.Clear();
            loadingThumbs.Clear();
            knownMediaPaths.Clear();
            foreach (var image in thumbCache.Values) image.Dispose();
            thumbCache.Clear();
            rowByPath.Clear();
            expectedMediaCount = 0;
            receivedMediaCount = 0;
            gallery.Items.Clear();
            gallery.Groups.Clear();
            images.Images.Clear();
            uiGroups.Clear();
            while (pendingMediaPaths.TryDequeue(out _)) { }
            while (pendingThumbnails.TryDequeue(out _)) { }
            thumbBridgeReady = false;
            mediaPage = 0;
            mediaScanComplete = false;

            await ReadBackendMedia();
            PumpMediaPaths();
            PumpMediaPaths();
            receivingMediaInfo = false;
            Render();
            gallery.Enabled = true;
            allButton.Enabled = true;
            progress.Maximum = Math.Max(1, allMedia.Count);
            progress.Value = progress.Maximum;
            ScheduleThumbnailLoad();
            librarySummary.Text = $"Всего файлов на телефоне: {allMedia.Count:N0}  ·  Последнее обновление: {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось прочитать медиатеку:\n\n" + ex.Message, "ФотоАрхив", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log("Load media", ex);
        }
        finally
        {
            receivingMediaInfo = false;
            mediaButton.Enabled = true;
            gallery.Enabled = true;
            allButton.Enabled = true;
            scanDialog?.Close();
            scanDialog = null;
            UpdateSelected();
        }
    }

    async Task ReadBackendMedia()
    {
        var pipeName = "PhotoArchive-Media-" + Guid.NewGuid().ToString("N");
        var backend = Path.Combine(AppContext.BaseDirectory, "PhotoArchive.Backend.exe");
        if (!File.Exists(backend)) throw new FileNotFoundException("Не найден PhotoArchive.Backend.exe");

        var start = new ProcessStartInfo(backend) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(pipeName);
        if (!string.IsNullOrWhiteSpace(serial)) start.ArgumentList.Add(serial);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить backend");
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var reader = new StreamReader(pipe);

        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("TOTAL\t", StringComparison.Ordinal))
            {
                if (!int.TryParse(line[6..], out var total)) continue;
                expectedMediaCount = total;
                scanDialog?.SetTotal(total);
                progress.Maximum = Math.Max(1, total);
                progress.Value = 0;
            }
            else if (line.StartsWith("COUNTING\t", StringComparison.Ordinal) && int.TryParse(line[9..], out var counting))
                scanDialog?.SetCounting(counting);
            else if (line.StartsWith("FILE\t", StringComparison.Ordinal))
            {
                pendingMediaPaths.Enqueue(line[5..]);
                receivedMediaCount++;
                if (expectedMediaCount > 0) progress.Value = Math.Min(expectedMediaCount, receivedMediaCount);
                scanDialog?.SetReceived(receivedMediaCount);
            }
            else if (line == "DONE")
            {
                receivingMediaInfo = false;
                scanDialog?.SetPreview();
                if (expectedMediaCount > 0) progress.Value = progress.Maximum;
            }
            else if (line.StartsWith("THUMB\t", StringComparison.Ordinal))
            {
                var parts = line.Split('\t', 3);
                if (parts.Length != 3) continue;
                try { pendingThumbnails.Enqueue((parts[1], Convert.FromBase64String(parts[2]))); }
                catch (Exception ex) { Log("Decode thumbnail " + parts[1], ex); }
            }
            else if (line.StartsWith("THUMB_ERROR\t", StringComparison.Ordinal))
                Debug.WriteLine("[PhotoArchive] Thumbnail unavailable: " + line[12..]);
            else if (line.StartsWith("ERROR\t", StringComparison.Ordinal))
                throw new InvalidOperationException(line[6..]);
        }

        await process.WaitForExitAsync();
    }

    async Task FetchNextMediaPage()
    {
        if (serial == null || mediaPageLoading || mediaScanComplete || !thumbBridgeReady) return;
        mediaPageLoading = true;
        try
        {
            var text = await BridgeList(mediaPage * MediaPageSize, MediaPageSize);
            var paths = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(IsMedia).Distinct().ToArray();
            foreach (var path in paths) pendingMediaPaths.Enqueue(path);
            mediaPage++;
            if (paths.Length < MediaPageSize) mediaScanComplete = true;
            PumpMediaPaths();
        }
        catch (Exception ex) { Log("Media page", ex); }
        finally { mediaPageLoading = false; }
    }

    static async Task<string> BridgeList(int offset, int limit)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 18765);
        var stream = client.GetStream();
        var request = Encoding.UTF8.GetBytes($"LIST\t{offset}\t{limit}");
        await stream.WriteAsync(new[] { (byte)(request.Length >> 8), (byte)request.Length });
        await stream.WriteAsync(request);
        await stream.FlushAsync();
        var header = await ReadExactly(stream, 2);
        if (header.Length != 2) return "";
        var length = (header[0] << 8) | header[1];
        if (length == 0) return "";
        return Encoding.UTF8.GetString(await ReadExactly(stream, length));
    }

    bool IsNearBottom()
    {
        try { return (gallery.TopItem?.Index ?? 0) + 35 >= gallery.Items.Count; }
        catch { return false; }
    }

    void PumpMediaPaths()
    {
        if (IsDisposed) return;
        var changed = false;
        var count = 0;
        while (count++ < 500 && pendingMediaPaths.TryDequeue(out var path))
        {
            if (!knownMediaPaths.Add(path)) continue;
            AddMedia(path);
            changed = true;
        }
        ApplyPendingThumbnails();
        if (!changed) return;
        if (!receivingMediaInfo) Render();
        ScheduleThumbnailLoad();
    }

    void ApplyPendingThumbnails()
    {
        while (pendingThumbnails.TryDequeue(out var thumb))
        {
            if (loadedThumbs.Contains(thumb.Path) || thumb.Bytes.Length == 0) continue;
            try
            {
                using var ms = new MemoryStream(thumb.Bytes);
                using var source = Image.FromStream(ms);
                var copy = CropThumbnail(source, images.ImageSize);
                if (thumbCache.Remove(thumb.Path, out var previous)) previous.Dispose();
                thumbCache[thumb.Path] = copy;
                loadedThumbs.Add(thumb.Path);
                if (rowByPath.TryGetValue(thumb.Path, out var row) && row.ListView == gallery)
                {
                    images.Images.Add(copy);
                    row.ImageIndex = images.Images.Count - 1;
                    gallery.Invalidate(row.Bounds);
                }
            }
            catch (Exception ex) { Log("Apply thumbnail " + thumb.Path, ex); }
        }
    }

    MediaItem AddMedia(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var video = ext is ".mp4" or ".mov" or ".mkv" or ".webm" or ".3gp";
        var group = ResolveGroupLabel(name);
        var item = new MediaItem(path, name, video, group);
        allMedia.Add(item);
        return item;
    }

    static string ResolveGroupLabel(string name)
    {
        var match = Regex.Match(name, @"(?<y>20\d{2})[-_]?(?<m>0[1-9]|1[0-2])[-_]?(?<d>0[1-9]|[12]\d|3[01])");
        if (!match.Success) return "Без даты";
        if (!int.TryParse(match.Groups["y"].Value, out var y) ||
            !int.TryParse(match.Groups["m"].Value, out var m) ||
            !int.TryParse(match.Groups["d"].Value, out var d)) return "Без даты";
        try
        {
            var date = new DateTime(y, m, d);
            var culture = CultureInfo.GetCultureInfo("ru-RU");
            var shortDay = date.ToString("ddd", culture).TrimEnd('.');
            return $"{date.Day} {date.ToString("MMMM", culture)} {date.Year} ({shortDay})";
        }
        catch { return "Без даты"; }
    }

    static bool IsMedia(string path) => Regex.IsMatch(path, @"\.(jpg|jpeg|png|webp|heic|gif|mp4|mov|mkv|webm|3gp)$", RegexOptions.IgnoreCase);

    void Render()
    {
        if (!gallery.IsHandleCreated) return;
        var visible = allMedia.Where(MatchesFilter).ToList();
        gallery.BeginUpdate();
        try
        {
            gallery.Items.Clear();
            gallery.Groups.Clear();
            images.Images.Clear();
            uiGroups.Clear();
            rowByPath.Clear();

            foreach (var group in visible.GroupBy(x => x.Group))
            {
                var header = new ListViewGroup($"{group.Key}   ({group.Count():N0})", HorizontalAlignment.Left);
                uiGroups[group.Key] = header;
                gallery.Groups.Add(header);
                foreach (var item in group)
                {
                    int imageIndex;
                    if (thumbCache.TryGetValue(item.Remote, out var cached))
                    {
                        images.Images.Add(cached);
                        imageIndex = images.Images.Count - 1;
                    }
                    else imageIndex = CreatePlaceholder(item.Video);

                    var row = new ListViewItem("", imageIndex)
                    {
                        Tag = item,
                        Group = header,
                        ToolTipText = item.Name + "\n" + item.Remote
                    };
                    gallery.Items.Add(row);
                    rowByPath[item.Remote] = row;
                    if (chosen.Contains(item.Remote)) row.Selected = true;
                }
            }
        }
        finally { gallery.EndUpdate(); }
        SetGallerySpacing();
        UpdateSelected();
    }

    bool MatchesFilter(MediaItem item) => filter == "Все" || (filter == "Видео" && item.Video) || (filter == "Фото" && !item.Video);

    int CreatePlaceholder(bool video)
    {
        var bmp = new Bitmap(images.ImageSize.Width, images.ImageSize.Height);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(AppTheme.SurfaceMuted);
        g.FillRectangle(background, 0, 0, bmp.Width, bmp.Height);
        using var soft = new SolidBrush(video ? Color.FromArgb(234, 232, 255) : Color.FromArgb(232, 239, 255));
        var icon = new Rectangle(bmp.Width / 2 - 27, bmp.Height / 2 - 27, 54, 54);
        g.FillRoundedRectangle(soft, icon, 16);
        using var pen = new Pen(ItemColor(video), 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (video)
            g.DrawPolygon(pen, new[] { new Point(icon.X + 21, icon.Y + 15), new Point(icon.X + 21, icon.Bottom - 15), new Point(icon.Right - 13, icon.Y + icon.Height / 2) });
        else
        {
            g.DrawRectangle(pen, icon.X + 13, icon.Y + 16, 29, 22);
            g.DrawEllipse(pen, icon.X + 18, icon.Y + 20, 6, 6);
        }
        images.Images.Add(bmp);
        return images.Images.Count - 1;

        static Color ItemColor(bool isVideo) => isVideo ? Color.FromArgb(103, 82, 214) : AppTheme.Primary;
    }

    void ScheduleThumbnailLoad()
    {
        if (thumbnailLoadQueued || !gallery.IsHandleCreated) return;
        thumbnailLoadQueued = true;
        BeginInvoke((Action)(() =>
        {
            thumbnailLoadQueued = false;
            LoadVisibleThumbnails();
        }));
    }

    async void LoadVisibleThumbnails()
    {
        ApplyPendingThumbnails();
        await Task.CompletedTask;
    }

    static Bitmap CropThumbnail(Image source, Size target)
    {
        var result = new Bitmap(target.Width, target.Height);
        using var g = Graphics.FromImage(result);
        g.Clear(AppTheme.SurfaceMuted);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var scale = Math.Max((double)target.Width / source.Width, (double)target.Height / source.Height);
        var w = (int)Math.Ceiling(source.Width * scale);
        var h = (int)Math.Ceiling(source.Height * scale);
        g.DrawImage(source, new Rectangle((target.Width - w) / 2, (target.Height - h) / 2, w, h));
        return result;
    }

    async Task<bool> EnsureThumbnailBridge()
    {
        try
        {
            await Adb.Run("-s", serial!, "forward", "tcp:18765", "tcp:8765");
            try { await Adb.Run("-s", serial!, "shell", "pm", "grant", "com.photoarchive.app", "android.permission.READ_MEDIA_IMAGES"); } catch { }
            try { await Adb.Run("-s", serial!, "shell", "pm", "grant", "com.photoarchive.app", "android.permission.READ_MEDIA_VIDEO"); } catch { }
            if (await StartBridgeAndConnect()) return true;
            var apk = Path.Combine(AppContext.BaseDirectory, "thumbnail-bridge.apk");
            if (!File.Exists(apk)) throw new FileNotFoundException("В EXE-папке отсутствует thumbnail-bridge.apk");
            await Adb.Run("-s", serial!, "install", "-r", apk);
            return await StartBridgeAndConnect();
        }
        catch (Exception ex) { Log("Thumbnail bridge", ex); }
        return false;
    }

    async Task<bool> StartBridgeAndConnect()
    {
        try { await Adb.Run("-s", serial!, "shell", "am", "start-foreground-service", "-n", "com.photoarchive.app/com.example.saftest.ThumbnailBridgeService"); }
        catch { return false; }
        for (var i = 0; i < 12; i++)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", 18765);
                return true;
            }
            catch { await Task.Delay(100); }
        }
        return false;
    }

    static async Task<byte[]> BridgeThumbnail(string path)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 18765);
        var stream = client.GetStream();
        var request = Encoding.UTF8.GetBytes(path);
        if (request.Length > ushort.MaxValue) return Array.Empty<byte>();
        await stream.WriteAsync(new[] { (byte)(request.Length >> 8), (byte)request.Length });
        await stream.WriteAsync(request);
        await stream.FlushAsync();
        var header = await ReadExactly(stream, 4);
        if (header.Length != 4) return Array.Empty<byte>();
        var length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        if (length <= 0 || length > 2_000_000) return Array.Empty<byte>();
        return await ReadExactly(stream, length);
    }

    static async Task<byte[]> ReadExactly(NetworkStream stream, int count)
    {
        var data = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(data.AsMemory(offset, count - offset));
            if (read == 0) break;
            offset += read;
        }
        return offset == count ? data : data[..offset];
    }

    async Task StartTransferAsync()
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            MessageBox.Show(this, "Телефон не найден через ADB. Нажмите «Обновить телефон» и подтвердите USB-отладку.", "ФотоАрхив", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var files = allMedia.Where(item => chosen.Contains(item.Remote)).ToList();
        if (files.Count == 0)
        {
            MessageBox.Show(this, "Сначала выберите фото или видео в медиатеке.", "ФотоАрхив", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(destination.Text)) return;

        using var cancellation = new CancellationTokenSource();
        using var dialog = new TransferProgressDialog(files.Count, destination.Text, cancellation);
        dialog.Show(this);
        sortButton.Enabled = false;
        var reporter = new Progress<TransferSnapshot>(dialog.Apply);
        try
        {
            await new TransferService().RunAsync(serial, files, destination.Text, reporter, cancellation.Token);
            if (!dialog.Visible) dialog.Show(this);
            dialog.BringToFront();
        }
        catch (OperationCanceledException) { dialog.ShowCancelled(); }
        catch (Exception ex) { dialog.ShowFailure(ex.Message); Log("Transfer", ex); }
        finally { sortButton.Enabled = true; }
    }

    void SelectAllVisible()
    {
        gallery.BeginUpdate();
        foreach (ListViewItem row in gallery.Items) row.Selected = true;
        gallery.EndUpdate();
        UpdateSelected();
    }

    void UpdateSelected()
    {
        selected.Text = $"Выбрано: {chosen.Count:N0} файлов";
        sortButton.Enabled = chosen.Count > 0;
    }

    static void Log(string where, Exception ex) => Debug.WriteLine($"[PhotoArchive] {where}: {ex}");
}

sealed class BufferedListView : ListView
{
    public BufferedListView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }
}

sealed class BrandMark : Control
{
    static readonly Image? RasterLogo = LoadRasterLogo();

    static Image? LoadRasterLogo()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "photoarchive-cloud.png");
            return File.Exists(path) ? Image.FromFile(path) : null;
        }
        catch { return null; }
    }

    public BrandMark() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (RasterLogo != null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(RasterLogo, new Rectangle(0, 0, Width - 1, Height - 1));
            return;
        }
        var box = new Rectangle(0, 0, Width - 1, Height - 1);
        using var background = new LinearGradientBrush(box, Color.FromArgb(95, 112, 234), Color.FromArgb(91, 78, 215), LinearGradientMode.Vertical);
        e.Graphics.FillRoundedRectangle(background, box, 14);
        using var pen = new Pen(Color.White, 2.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(pen, 7, 20, 15, 10, 180, 180);
        e.Graphics.DrawArc(pen, 14, 12, 19, 18, 205, 145);
        e.Graphics.DrawArc(pen, 27, 18, 14, 12, 205, 150);
        e.Graphics.DrawLine(pen, 10, 28, 37, 28);
    }
}

enum ChromeButtonKind { Minimize, Maximize, Close }

sealed class ChromeButton : Control
{
    readonly ChromeButtonKind kind;
    bool hover;

    public ChromeButton(ChromeButtonKind kind)
    {
        this.kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (hover)
        {
            using var hoverBrush = new SolidBrush(kind == ChromeButtonKind.Close ? Color.FromArgb(255, 238, 241) : AppTheme.SurfaceMuted);
            e.Graphics.FillRoundedRectangle(hoverBrush, new Rectangle(3, 3, Width - 6, Height - 6), 9);
        }
        using var pen = new Pen(kind == ChromeButtonKind.Close && hover ? AppTheme.Danger : AppTheme.TextSecondary, 1.2f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = Width / 2;
        var cy = Height / 2;
        if (kind == ChromeButtonKind.Minimize) e.Graphics.DrawLine(pen, cx - 7, cy + 3, cx + 7, cy + 3);
        else if (kind == ChromeButtonKind.Maximize) e.Graphics.DrawRectangle(pen, cx - 5, cy - 5, 10, 10);
        else
        {
            e.Graphics.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            e.Graphics.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }
    }
}

sealed class DesignedProgressBar : Control
{
    int maximum = 1;
    int value;
    bool indeterminate;
    readonly System.Windows.Forms.Timer animation = new() { Interval = 35 };
    int offset;

    public int Maximum { get => maximum; set { maximum = Math.Max(1, value); this.value = Math.Min(this.value, maximum); Invalidate(); } }
    public int Value { get => value; set { this.value = Math.Clamp(value, 0, maximum); Invalidate(); } }
    public bool Indeterminate { get => indeterminate; set { indeterminate = value; animation.Enabled = value; Invalidate(); } }

    public DesignedProgressBar()
    {
        Height = 14;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        animation.Tick += (_, _) => { offset = (offset + 8) % 180; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 2, Math.Max(1, Width - 1), Math.Max(8, Height - 4));
        using var empty = new SolidBrush(Color.FromArgb(232, 230, 244));
        e.Graphics.FillRoundedRectangle(empty, track, track.Height);
        Rectangle fill = Indeterminate
            ? new Rectangle(offset - 90, 2, 90, track.Height)
            : new Rectangle(0, 2, (int)(track.Width * (value / (double)Math.Max(1, maximum))), track.Height);
        if (fill.Width <= 0) return;
        fill.Intersect(track);
        if (fill.Width <= 0 || fill.Height <= 0) return;
        using var brush = new LinearGradientBrush(fill, Color.FromArgb(77, 104, 232), Color.FromArgb(139, 91, 225), LinearGradientMode.Horizontal);
        e.Graphics.FillRoundedRectangle(brush, fill, track.Height);
    }
}

sealed class ScanProgressDialog : Form
{
    readonly Label title = new();
    readonly Label details = new();
    readonly DesignedProgressBar bar = new();

    public ScanProgressDialog(Form owner)
    {
        Text = "ФотоАрхив — подготовка медиатеки";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ControlBox = false;
        ClientSize = new Size(500, 176);
        BackColor = AppTheme.Surface;
        Font = AppTheme.BodyFont();
        Padding = new Padding(28);

        title.Text = "Сканируем телефон";
        title.Font = AppTheme.HeadingFont(15f);
        title.ForeColor = AppTheme.TextPrimary;
        title.Location = new Point(28, 26);
        title.AutoSize = true;
        details.Text = "Подготавливаем список файлов…";
        details.ForeColor = AppTheme.TextSecondary;
        details.Location = new Point(29, 62);
        details.AutoSize = true;
        bar.Location = new Point(29, 108);
        bar.Size = new Size(442, 16);
        Controls.AddRange(new Control[] { title, details, bar });
    }

    public void SetCounting(int count)
    {
        title.Text = "Сканируем телефон";
        details.Text = $"Найдено файлов: {count:N0}";
        bar.Indeterminate = false;
        bar.Maximum = Math.Max(1, count);
        bar.Value = count;
    }

    public void SetTotal(int total)
    {
        title.Text = "Получаем список файлов";
        details.Text = $"Получено: 0 / {total:N0}";
        bar.Indeterminate = false;
        bar.Maximum = Math.Max(1, total);
        bar.Value = 0;
    }

    public void SetReceived(int count)
    {
        if (bar.Maximum > 1) bar.Value = Math.Min(bar.Maximum, count);
        details.Text = $"Получено: {count:N0} / {bar.Maximum:N0}";
    }

    public void SetPreview()
    {
        title.Text = "Список получен";
        details.Text = "Загружаем превью файлов…";
        bar.Value = bar.Maximum;
    }
}

sealed class RoundButton : Button
{
    bool hover;
    bool pressed;

    public int Radius { get; set; } = 12;
    public int BorderWidth { get; set; }
    public Color BorderColor { get; set; } = Color.Transparent;

    public RoundButton(string text)
    {
        Text = text;
        AutoSize = false;
        Height = 38;
        Width = Math.Max(140, text.Length * 8 + 34);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = AppTheme.Primary;
        ForeColor = Color.White;
        Font = AppTheme.ButtonFont(9.5f);
        Cursor = Cursors.Hand;
        Margin = new Padding(5, 2, 5, 2);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var fill = Enabled ? BackColor : Color.FromArgb(238, 239, 246);
        if (Enabled && hover) fill = Blend(fill, Color.Black, 0.035f);
        if (Enabled && pressed) fill = Blend(fill, Color.Black, 0.075f);
        using var path = RoundedPath(rect, Radius);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (BorderWidth > 0)
        {
            using var border = new Pen(BorderColor, BorderWidth);
            e.Graphics.DrawPath(border, path);
        }
        var textColor = Enabled ? ForeColor : AppTheme.TextMuted;
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            a.A,
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }

    static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
        path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
        path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = Rounded(rectangle, radius);
        g.FillPath(brush, path);
    }

    static GraphicsPath Rounded(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90);
        path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
        path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

static class Adb
{
    static string Exe => new[]
    {
        @"C:\Android\Sdk\platform-tools\adb.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"),
        "adb.exe"
    }.FirstOrDefault(File.Exists) ?? "adb.exe";

    public static Task<string> Run(params string[] args) => Task.Run(() => Execute(args));

    public static async Task<string> RunAsync(CancellationToken cancellationToken, params string[] args)
    {
        using var process = StartProcess(args);
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return output;
    }

    public static Process StartProcess(params string[] args)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        return new Process { StartInfo = psi };
    }

    public static Task<byte[]> Bytes(params string[] args) => Task.Run(() => ExecuteBytes(args));

    public static async Task StreamLines(string[] args, Action<string> onLine)
    {
        await Task.Run(async () =>
        {
            using var process = StartProcess(args);
            process.Start();
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var value = line.Trim();
                if (value.Length > 0) onLine(value);
            }
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        });
    }

    static string Execute(string[] args)
    {
        using var process = StartProcess(args);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return output;
    }

    static byte[] ExecuteBytes(string[] args)
    {
        using var process = StartProcess(args);
        process.Start();
        using var ms = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(ms);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        return ms.ToArray();
    }
}
