using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
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
    readonly RoundButton sortButton = new("Разложить по годам / месяцам / дням");
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
    readonly ConcurrentQueue<string> pendingMediaPaths = new();
    readonly ConcurrentQueue<(string Path, byte[] Bytes)> pendingThumbnails = new();
    int expectedMediaCount;
    int receivedMediaCount;
    readonly System.Windows.Forms.Timer mediaPump = new() { Interval = 20 };
    int renderVersion;
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
        MinimumSize = new Size(980, 680);
        Size = new Size(1220, 820);
        BackColor = AppTheme.Background;
        Font = AppTheme.BodyFont();
        Resize += (_, _) => ApplyWindowShape();

        images.ColorDepth = ColorDepth.Depth32Bit;
        images.ImageSize = new Size(196, 132);
        gallery.Dock = DockStyle.Fill;
        gallery.View = View.LargeIcon;
        gallery.LargeImageList = images;
        gallery.BorderStyle = BorderStyle.None;
        gallery.BackColor = AppTheme.Surface;
        gallery.MultiSelect = true;
        gallery.ItemSelectionChanged += (_, e) => { if (e.IsSelected) chosen.Add(((MediaItem)e.Item.Tag!).Remote); else chosen.Remove(((MediaItem)e.Item.Tag!).Remote); UpdateSelected(); };
        gallery.MouseWheel += (_, _) => BeginInvoke((Action)LoadVisibleThumbnails);

        refresh.Click += async (_, _) => await RefreshDevices();
        mediaButton.Click += async (_, _) => await LoadMedia();
        allButton.Click += (_, _) => SelectAllVisible();
        sortButton.Text = "Начать сортировку";
        sortButton.Width = 220;
        sortButton.Click += async (_, _) => await StartTransferAsync();
        devices.DropDownStyle = ComboBoxStyle.DropDownList;
        devices.FlatStyle = FlatStyle.Flat;
        devices.BackColor = AppTheme.Surface;
        devices.ForeColor = AppTheme.TextPrimary;
        devices.Width = 250;
        devices.SelectedIndexChanged += (_, _) => { };
        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ФотоАрхив");
        destination.Dock = DockStyle.Fill;
        destination.BorderStyle = BorderStyle.FixedSingle;
        destination.BackColor = AppTheme.Surface;
        destination.ForeColor = AppTheme.TextPrimary;
        destination.Margin = new Padding(12, 8, 12, 8);
        status.AutoSize = false;
        status.Text = "Телефон не найден — подтвердите USB-отладку";
        status.ForeColor = AppTheme.TextSecondary;
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        progress.Dock = DockStyle.Fill;
        progress.Maximum = 1; progress.Value = 0; progress.Indeterminate = false;

        Controls.Add(BuildModernLayout());
        mediaPump.Tick += (_, _) => PumpMediaPaths();
        mediaPump.Start();
        Shown += async (_, _) => await RefreshDevices();
    }

    Control BuildModernLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, BackColor = AppTheme.Background, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var titlebar = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background, Margin = new Padding(-16, -16, -16, 0), Padding = new Padding(20, 0, 10, 0) };
        var windowTitle = new Label { Text = "☁  ФотоАрхив", AutoSize = true, Font = AppTheme.HeadingFont(10.5f), ForeColor = AppTheme.TextSecondary, Location = new Point(20, 12), Cursor = Cursors.SizeAll };
        var close = new Label { Text = "×", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 18), ForeColor = AppTheme.TextSecondary, Width = 40, Dock = DockStyle.Right, Cursor = Cursors.Hand };
        var maximize = new Label { Text = "□", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11), ForeColor = AppTheme.TextSecondary, Width = 40, Dock = DockStyle.Right, Cursor = Cursors.Hand };
        var minimize = new Label { Text = "—", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 13), ForeColor = AppTheme.TextSecondary, Width = 40, Dock = DockStyle.Right, Cursor = Cursors.Hand };
        close.Click += (_, _) => Close(); minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximize.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        titlebar.Controls.AddRange(new Control[] { windowTitle, minimize, maximize, close });
        titlebar.Visible = false;
        foreach (Control dragControl in new Control[] { titlebar, windowTitle })
            dragControl.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
        root.Controls.Add(titlebar, 0, 0);

        var header = new RoundedPanel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Radius = 18, Padding = new Padding(24, 16, 24, 14), Margin = new Padding(0, 0, 0, 10) };
        var title = new Label { Text = "ФотоАрхив", AutoSize = false, Font = AppTheme.TitleFont(24), ForeColor = AppTheme.TextPrimary, Location = new Point(56, 10), Size = new Size(360, 36) };
        var subtitle = new Label { Text = "Сортировка фото и видео с телефона по датам", AutoSize = false, Font = AppTheme.BodyFont(10.5f), ForeColor = AppTheme.TextSecondary, Location = new Point(57, 48), Size = new Size(420, 22) };
        var logo = new BrandMark { Location = new Point(9, 15), Size = new Size(42, 42) };
        var closeButton = new ChromeButton(ChromeButtonKind.Close) { Width = 42, Height = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right, Cursor = Cursors.Hand };
        var maximizeButton = new ChromeButton(ChromeButtonKind.Maximize) { Width = 42, Height = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right, Cursor = Cursors.Hand };
        var minimizeButton = new ChromeButton(ChromeButtonKind.Minimize) { Width = 42, Height = 34, Anchor = AnchorStyles.Top | AnchorStyles.Right, Cursor = Cursors.Hand };
        void PositionWindowButtons()
        {
            closeButton.Location = new Point(header.ClientSize.Width - 54, 8);
            maximizeButton.Location = new Point(header.ClientSize.Width - 96, 8);
            minimizeButton.Location = new Point(header.ClientSize.Width - 138, 8);
        }
        closeButton.Click += (_, _) => Close(); minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maximizeButton.Click += (_, _) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        header.Resize += (_, _) => PositionWindowButtons();
        header.Controls.AddRange(new Control[] { logo, title, subtitle, minimizeButton, maximizeButton, closeButton });
        PositionWindowButtons();
        foreach (Control dragControl in new Control[] { header, logo, title, subtitle })
            dragControl.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); } };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, WrapContents = false, BackColor = AppTheme.Surface, Padding = new Padding(0, 4, 0, 0) };
        devices.Height = 42;
        refresh.Text = "↻  Обновить телефон"; mediaButton.Text = "▧  Медиатека"; allButton.Text = "☑  Выбрать всё";
        tools.Controls.AddRange(new Control[] { devices, refresh, mediaButton, allButton });
        tools.Controls.Add(new Label { Text = "ⓘ  Справка", AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.ButtonFont(), Cursor = Cursors.Hand, Padding = new Padding(20, 12, 0, 0) });
        header.Controls.Add(tools);
        root.Controls.Add(header, 0, 1);

        var toolbar = new RoundedPanel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Radius = 14, Padding = new Padding(16, 10, 16, 8), Margin = new Padding(0, 0, 0, 8) };
        var filters = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 650, BackColor = AppTheme.Surface, WrapContents = false };
        filters.Controls.AddRange(new Control[]
        {
            FilterButton("Все файлы", true, () => { filter = "Все"; Render(); }),
            FilterButton("Фото", false, () => { filter = "Фото"; Render(); }),
            FilterButton("Видео", false, () => { filter = "Видео"; Render(); }),
            FilterButton("▣  За всё время ⌄", false, () => { }, 160),
            FilterButton("☷  Фильтры", false, () => { }, 120)
        });
        selected.Text = "Выбрано: 0 файлов"; selected.Dock = DockStyle.Right; selected.Width = 220; selected.TextAlign = ContentAlignment.MiddleRight; selected.ForeColor = AppTheme.TextSecondary; selected.Font = AppTheme.ButtonFont();
        toolbar.Controls.Add(filters); toolbar.Controls.Add(selected);
        root.Controls.Add(toolbar, 0, 2);

        gallery.BackColor = AppTheme.Surface; gallery.Margin = new Padding(0); gallery.HideSelection = false; gallery.ShowItemToolTips = true;
        root.Controls.Add(gallery, 0, 3);

        var destinationPanel = new RoundedPanel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Radius = 16, Padding = new Padding(18, 15, 18, 15), Margin = new Padding(0, 8, 0, 6) };
        var folderIcon = new Label { Text = "▱", Font = new Font("Segoe Fluent Icons", 23), ForeColor = AppTheme.Primary, AutoSize = true, Location = new Point(20, 28) };
        var destinationCaption = new Label { Text = "Папка назначения", AutoSize = true, Font = AppTheme.BodyFont(9), ForeColor = AppTheme.TextSecondary, Location = new Point(72, 17) };
        destination.Location = new Point(70, 42); destination.Width = 620; destination.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        var chooseFolder = new RoundButton("Изменить  ✎") { Width = 142, Height = 42, Anchor = AnchorStyles.Top | AnchorStyles.Right, BackColor = AppTheme.PrimarySoft, ForeColor = AppTheme.Primary };
        chooseFolder.Click += (_, _) => { using var d = new FolderBrowserDialog { Description = "Куда сохранить фотоархив?", SelectedPath = destination.Text }; if (d.ShowDialog() == DialogResult.OK) destination.Text = d.SelectedPath; };
        sortButton.Height = 52; sortButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        void PositionDestinationActions()
        {
            chooseFolder.Location = new Point(Math.Max(520, destinationPanel.ClientSize.Width - 410), 27);
            sortButton.Location = new Point(Math.Max(680, destinationPanel.ClientSize.Width - 244), 22);
            destination.Width = Math.Max(320, chooseFolder.Left - 90);
        }
        destinationPanel.Resize += (_, _) => PositionDestinationActions();
        destinationPanel.Controls.AddRange(new Control[] { folderIcon, destinationCaption, destination, chooseFolder, sortButton });
        PositionDestinationActions();
        root.Controls.Add(destinationPanel, 0, 4);

        var statusBar = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };
        connectionSummary.Text = "●  Телефон не подключён"; connectionSummary.ForeColor = AppTheme.TextSecondary; connectionSummary.Dock = DockStyle.Left; connectionSummary.Width = 360; connectionSummary.TextAlign = ContentAlignment.MiddleLeft;
        librarySummary.Text = "Всего файлов на телефоне: —"; librarySummary.ForeColor = AppTheme.TextSecondary; librarySummary.Dock = DockStyle.Right; librarySummary.Width = 420; librarySummary.TextAlign = ContentAlignment.MiddleRight;
        status.Visible = false; progress.Visible = false;
        statusBar.Controls.Add(connectionSummary); statusBar.Controls.Add(librarySummary);
        root.Controls.Add(statusBar, 0, 5);
        return root;
    }

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);

    void ApplyWindowShape()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            // A borderless form keeps its previous Region when maximized. That
            // clips the restored custom header and makes the window controls
            // disappear. Maximized windows must use the full native work area.
            Region = null;
            return;
        }
        if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
        using var path = new GraphicsPath();
        const int radius = 18;
        path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
        path.AddArc(ClientSize.Width - radius * 2 - 1, 0, radius * 2, radius * 2, 270, 90);
        path.AddArc(ClientSize.Width - radius * 2 - 1, ClientSize.Height - radius * 2 - 1, radius * 2, radius * 2, 0, 90);
        path.AddArc(0, ClientSize.Height - radius * 2 - 1, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    RoundButton FilterButton(string text, bool active, Action action, int width = 104)
    {
        var button = new RoundButton(text) { Width = width, Height = 38, BackColor = active ? AppTheme.PrimarySoft : AppTheme.Surface, ForeColor = active ? AppTheme.Primary : AppTheme.TextSecondary };
        button.Click += (_, _) => action();
        return button;
    }

    Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = AppTheme.Background, Padding = new Padding(AppTheme.Space3) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new Padding(28, 17, 28, 10) };
        var title = new Label { Text = "ФотоАрхив", AutoSize = true, Font = AppTheme.TitleFont(25), ForeColor = AppTheme.TextPrimary, Location = new Point(74, 14) };
        var logo = new BrandMark { Location = new Point(28, 13), Size = new Size(38, 38) };
        header.Controls.Add(logo);
        header.Controls.Add(title);
        var subtitle = new Label { Text = "Сортировка фото и видео с телефона по датам", AutoSize = true, Font = AppTheme.BodyFont(10.5f), ForeColor = AppTheme.TextSecondary, Location = new Point(75, 53) };
        header.Controls.Add(subtitle);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, BackColor = AppTheme.Surface, Padding = new Padding(0, 3, 0, 0) };
        tools.Controls.Add(devices); tools.Controls.Add(refresh); tools.Controls.Add(mediaButton); tools.Controls.Add(allButton);
        header.Controls.Add(tools);
        root.Controls.Add(header, 0, 0);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface, Padding = new Padding(18, 10, 18, 8), WrapContents = false };
        var allFilter = new RoundButton("Все файлы") { Width = 118, Height = 36, BackColor = AppTheme.Primary };
        var photoFilter = new RoundButton("Фото") { Width = 92, Height = 36, BackColor = AppTheme.PrimarySoft, ForeColor = AppTheme.TextSecondary };
        var videoFilter = new RoundButton("Видео") { Width = 92, Height = 36, BackColor = AppTheme.PrimarySoft, ForeColor = AppTheme.TextSecondary };
        var period = new RoundButton("За всё время  ▾") { Width = 150, Height = 36, BackColor = AppTheme.Surface, ForeColor = AppTheme.TextPrimary };
        allFilter.Click += (_, _) => { filter = "Все"; Render(); };
        photoFilter.Click += (_, _) => { filter = "Фото"; Render(); };
        videoFilter.Click += (_, _) => { filter = "Видео"; Render(); };
        filters.Controls.AddRange(new Control[] { allFilter, photoFilter, videoFilter, period });
        selected.Text = "Выбрано: 0 файлов"; selected.AutoSize = true; selected.ForeColor = AppTheme.TextSecondary; selected.Padding = new Padding(20, 10, 0, 0);
        filters.Controls.Add(selected);
        root.Controls.Add(filters, 0, 1);
        gallery.BackColor = AppTheme.Surface;
        root.Controls.Add(gallery, 0, 2);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(16, 10, 16, 10), BackColor = AppTheme.Surface };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        footer.Controls.Add(destination, 0, 0); footer.SetColumnSpan(destination, 2);
        var chooseFolder = new RoundButton("Выбрать папку") { Dock = DockStyle.Fill, Margin = new Padding(8, 6, 8, 6) };
        chooseFolder.Click += (_, _) => { using var d = new FolderBrowserDialog { Description = "Куда сохранить фотоархив?", SelectedPath = destination.Text }; if (d.ShowDialog() == DialogResult.OK) destination.Text = d.SelectedPath; };
        footer.Controls.Add(chooseFolder, 2, 0); footer.Controls.Add(status, 0, 1); footer.SetColumnSpan(status, 2); footer.Controls.Add(progress, 2, 1);
        root.Controls.Add(footer, 0, 3);
        return root;
    }

    async Task RefreshDevices()
    {
        refresh.Enabled = false; status.Text = "Проверяем ADB…";
        try
        {
            var found = await Task.Run(() => MediaDeviceManager.Instance.GetDevices().ToList());
            devices.Items.Clear(); devices.Items.AddRange(found.Select(x => x.FriendlyName).ToArray());
            if (found.Count > 0)
            {
                devices.SelectedIndex = 0; mtpDevice = found[0];
                try
                {
                    var adb = await Adb.Run("devices");
                    serial = adb.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => Regex.IsMatch(x, @"\s+device$"))
                        .Select(x => Regex.Split(x, @"\s+")[0]).FirstOrDefault();
                }
                catch { serial = null; }
            }
            status.Text = found.Count == 0 ? "Телефон не найден — включите режим передачи файлов" : $"MTP подключен: {found.Count}";
        }
        catch (Exception ex) { status.Text = "Ошибка ADB: " + ex.Message; Log("ADB refresh", ex); }
        finally
        {
            refresh.Enabled = true;
            connectionSummary.Text = mtpDevice == null ? "●  Телефон не подключён" : $"●  Телефон подключён: {devices.SelectedItem}";
            connectionSummary.ForeColor = mtpDevice == null ? AppTheme.TextSecondary : AppTheme.Success;
        }
    }

    async Task LoadMedia()
    {
        if (mtpDevice == null) { status.Text = "Сначала подключите телефон по USB в режиме передачи файлов"; return; }
        mediaButton.Enabled = false; allButton.Enabled = false; gallery.Enabled = false; receivingMediaInfo = true;
        progress.Indeterminate = false; progress.Maximum = 1; progress.Value = 0;
        status.Text = "Подготавливаем подсчёт файлов. Дождитесь окончания получения информации…";
        scanDialog = new ScanProgressDialog(this); scanDialog.Show(this);
        try
        {
            allMedia.Clear(); chosen.Clear(); loadedThumbs.Clear(); loadingThumbs.Clear(); knownMediaPaths.Clear(); thumbCache.Clear();
            expectedMediaCount = 0; receivedMediaCount = 0;
            gallery.Items.Clear(); gallery.Groups.Clear(); images.Images.Clear(); uiGroups.Clear();
            while (pendingMediaPaths.TryDequeue(out _)) { }
            thumbBridgeReady = false;
            await ReadBackendMedia();
            PumpMediaPaths();
            PumpMediaPaths();
            receivingMediaInfo = false; gallery.Enabled = true; allButton.Enabled = true; progress.Indeterminate = false;
            progress.Maximum = Math.Max(1, allMedia.Count); progress.Value = progress.Maximum;
            ScheduleThumbnailLoad();
            librarySummary.Text = $"Всего файлов на телефоне: {allMedia.Count:N0}  ·  Обновлено: {DateTime.Now:HH:mm}";
            status.Text = expectedMediaCount > 0
                ? $"Список получен: {allMedia.Count:N0} / {expectedMediaCount:N0}. Загружаем превью…"
                : $"Список получен: {allMedia.Count:N0}. Загружаем превью…";
        }
        catch (Exception ex) { status.Text = "Ошибка чтения медиатеки: " + ex.Message; Log("Load media", ex); }
        finally { receivingMediaInfo = false; mediaButton.Enabled = true; gallery.Enabled = true; allButton.Enabled = true; scanDialog?.Close(); scanDialog = null; }
    }

    async Task ReadBackendMedia()
    {
        var pipeName = "PhotoArchive-Media-" + Guid.NewGuid().ToString("N");
        var backend = Path.Combine(AppContext.BaseDirectory, "PhotoArchive.Backend.exe");
        if (!File.Exists(backend)) throw new FileNotFoundException("Не найден PhotoArchive.Backend.exe");
        var start = new ProcessStartInfo(backend) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(pipeName);
        if (!string.IsNullOrWhiteSpace(serial)) start.ArgumentList.Add(serial);
        using var process = Process.Start(start);
        if (process == null) throw new InvalidOperationException("Не удалось запустить backend");
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000); using var reader = new StreamReader(pipe);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.StartsWith("TOTAL\t", StringComparison.Ordinal))
            {
                if (int.TryParse(line[6..], out var total))
                {
                    expectedMediaCount = total;
                    scanDialog?.SetTotal(total);
                    progress.Indeterminate = false;
                    progress.Maximum = Math.Max(1, total); progress.Value = 0;
                    status.Text = $"Всего найдено: {total:N0}. Получаем список: 0 / {total:N0}";
                }
            }
            else if (line.StartsWith("COUNTING\t", StringComparison.Ordinal) && int.TryParse(line[9..], out var counting))
            {
                status.Text = $"Сканируем телефон… собрано файлов: {counting:N0}";
                scanDialog?.SetCounting(counting);
            }
            else if (line.StartsWith("FILE\t", StringComparison.Ordinal))
            {
                pendingMediaPaths.Enqueue(line[5..]);
                if (expectedMediaCount > 0) progress.Value = Math.Min(expectedMediaCount, ++receivedMediaCount);
                if (expectedMediaCount > 0) status.Text = $"Получаем список файлов: {receivedMediaCount:N0} / {expectedMediaCount:N0}";
                scanDialog?.SetReceived(receivedMediaCount);
            }
            else if (line == "DONE")
            {
                receivingMediaInfo = false;
                if (expectedMediaCount > 0)
                {
                    scanDialog?.SetPreview();
                    progress.Indeterminate = false;
                    progress.Value = progress.Maximum;
                    status.Text = $"Список получен: {expectedMediaCount:N0} / {expectedMediaCount:N0}. Загружаем превью…";
                }
            }
            else if (line.StartsWith("THUMB\t", StringComparison.Ordinal))
            {
                var parts = line.Split('\t', 3);
                if (parts.Length == 3)
                {
                    try { pendingThumbnails.Enqueue((parts[1], Convert.FromBase64String(parts[2]))); }
                    catch (Exception ex) { Log("Decode thumbnail " + parts[1], ex); }
                }
            }
            else if (line.StartsWith("THUMB_ERROR\t", StringComparison.Ordinal))
                Debug.WriteLine("[PhotoArchive] Thumbnail unavailable: " + line[12..]);
            else if (line.StartsWith("ERROR\t", StringComparison.Ordinal)) throw new InvalidOperationException(line[6..]);
        }
        await process.WaitForExitAsync();
    }

    async Task FetchNextMediaPage()
    {
        if (serial == null || mediaPageLoading || mediaScanComplete) return;
        mediaPageLoading = true; mediaButton.Enabled = false;
        try
        {
            var text = thumbBridgeReady ? await BridgeList(mediaPage * MediaPageSize, MediaPageSize) : "";
            var paths = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(IsMedia).Distinct().ToArray();
            foreach (var path in paths) pendingMediaPaths.Enqueue(path);
            mediaPage++; if (paths.Length < MediaPageSize) mediaScanComplete = true;
            PumpMediaPaths();
            status.Text = mediaScanComplete ? $"Загружено файлов: {allMedia.Count}. Дальше — по прокрутке." : $"Показано файлов: {allMedia.Count}. Прокрутите вниз для следующей страницы.";
        }
        catch (Exception ex) { status.Text = "Ошибка страницы медиатеки: " + ex.Message; Log("Media page", ex); }
        finally { mediaPageLoading = false; mediaButton.Enabled = true; }
    }

    static async Task<string> BridgeList(int offset, int limit)
    {
        using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", 18765);
        var stream = client.GetStream(); var request = Encoding.UTF8.GetBytes($"LIST\t{offset}\t{limit}");
        await stream.WriteAsync(new[] { (byte)(request.Length >> 8), (byte)request.Length }); await stream.WriteAsync(request); await stream.FlushAsync();
        var header = await ReadExactly(stream, 2); if (header.Length != 2) return "";
        var length = (header[0] << 8) | header[1]; if (length == 0) return "";
        var data = await ReadExactly(stream, length); return Encoding.UTF8.GetString(data);
    }

    bool IsNearBottom()
    {
        try { return (gallery.TopItem?.Index ?? 0) + 35 >= gallery.Items.Count; }
        catch { return false; }
    }

    void PumpMediaPaths()
    {
        if (IsDisposed) return;
        var changed = false; var count = 0;
        gallery.BeginUpdate();
        while (count++ < 500 && pendingMediaPaths.TryDequeue(out var path))
        {
            if (!knownMediaPaths.Add(path)) continue;
            AddMedia(path); changed = true;
        }
        ApplyPendingThumbnails();
        gallery.EndUpdate();
        if (!changed) return;
        ScheduleThumbnailLoad();
        if (receivingMediaInfo && expectedMediaCount > 0)
            status.Text = $"Получаем список файлов: {receivedMediaCount:N0} / {expectedMediaCount:N0}";
    }

    void ApplyPendingThumbnails()
    {
        while (pendingThumbnails.TryDequeue(out var thumb))
        {
            if (loadedThumbs.Contains(thumb.Path)) continue;
            var row = gallery.Items.Cast<ListViewItem>().FirstOrDefault(x => ((MediaItem)x.Tag!).Remote == thumb.Path);
            if (thumb.Bytes.Length == 0) continue;
            var itemForThumb = allMedia.FirstOrDefault(x => x.Remote == thumb.Path);
            if (itemForThumb == null)
            {
                pendingThumbnails.Enqueue(thumb);
                break;
            }
            try
            {
                using var ms = new MemoryStream(thumb.Bytes);
                using var source = Image.FromStream(ms);
                var copy = CropThumbnail(source, images.ImageSize);
                thumbCache[thumb.Path] = copy;
                images.Images.Add(copy);
                if (row == null)
                {
                    var item = itemForThumb;
                    if (!uiGroups.TryGetValue(item.Group, out var group))
                    {
                        group = new ListViewGroup(item.Group, HorizontalAlignment.Left);
                        uiGroups[item.Group] = group; gallery.Groups.Add(group);
                    }
                    row = new ListViewItem(item.Name, images.Images.Count - 1)
                    { Tag = item, Group = group, ToolTipText = item.Remote };
                    gallery.Items.Add(row);
                }
                else row.ImageIndex = images.Images.Count - 1;
                loadedThumbs.Add(thumb.Path);
            }
            catch (Exception ex) { Log("Apply thumbnail " + thumb.Path, ex); }
        }
    }

    MediaItem AddMedia(string path)
    {
        var name = Path.GetFileName(path); var ext = Path.GetExtension(name).ToLowerInvariant();
        var video = ext is ".mp4" or ".mov" or ".mkv" or ".webm" or ".3gp";
        var match = Regex.Match(name, @"(20\d{2})[\-_]?(\d{2})?[\-_]?(\d{2})?");
        var group = match.Success ? match.Groups[1].Value + (match.Groups[2].Success ? " / " + match.Groups[2].Value : "") : "Без даты";
        var item = new MediaItem(path, name, video, group); allMedia.Add(item); return item;
    }

    void AddMediaTile(MediaItem item)
    {
        if (!uiGroups.TryGetValue(item.Group, out var group))
        {
            group = new ListViewGroup(item.Group, HorizontalAlignment.Left); uiGroups[item.Group] = group; gallery.Groups.Add(group);
        }
        var row = new ListViewItem(item.Name, CreatePlaceholder(item.Video)) { Tag = item, Group = group, ToolTipText = item.Remote };
        gallery.Items.Add(row); if (chosen.Contains(item.Remote)) row.Selected = true;
    }

    void ScheduleThumbnailLoad()
    {
        if (thumbnailLoadQueued || !gallery.IsHandleCreated) return;
        thumbnailLoadQueued = true;
        BeginInvoke((Action)(() => { thumbnailLoadQueued = false; LoadVisibleThumbnails(); }));
    }

    static bool IsMedia(string path) => Regex.IsMatch(path, @"\.(jpg|jpeg|png|webp|heic|gif|mp4|mov|mkv|webm|3gp)$", RegexOptions.IgnoreCase);

    void Render()
    {
        renderVersion++;
        gallery.BeginUpdate(); gallery.Items.Clear(); images.Images.Clear();
        var visible = allMedia.Where(x => filter == "Все" || (filter == "Видео" && x.Video) || (filter == "Фото" && !x.Video)).ToList();
        var groups = visible.GroupBy(x => x.Group);
        foreach (var group in groups)
        {
            var header = new ListViewGroup(group.Key, HorizontalAlignment.Left); gallery.Groups.Add(header);
            foreach (var item in group)
            {
                var row = new ListViewItem(item.Name, CreatePlaceholder(item.Video)) { Tag = item, Group = header, ToolTipText = item.Remote };
                gallery.Items.Add(row);
                if (chosen.Contains(item.Remote)) row.Selected = true;
            }
        }
        gallery.EndUpdate(); UpdateSelected();
        if (gallery.IsHandleCreated) BeginInvoke((Action)LoadVisibleThumbnails);
    }

    int CreatePlaceholder(bool video)
    {
        using var bmp = new Bitmap(118, 118); using var g = Graphics.FromImage(bmp); g.Clear(Color.FromArgb(235, 235, 246));
        using var brush = new SolidBrush(video ? Color.FromArgb(108, 92, 210) : Color.FromArgb(83, 111, 218));
        g.FillRoundedRectangle(brush, new Rectangle(32, 32, 54, 54), 14);
        using var p = new Pen(Color.White, 4); if (video) g.DrawPolygon(p, new[] { new Point(51, 45), new Point(73, 59), new Point(51, 73) }); else { g.DrawRectangle(p, 44, 47, 30, 23); g.DrawEllipse(p, 49, 50, 7, 7); }
        images.Images.Add(bmp); return images.Images.Count - 1;
    }

    async void LoadVisibleThumbnails()
    {
        // Thumbnails are received from the backend over the pipe. Keeping a
        // second MTP session here made previews fail intermittently.
        ApplyPendingThumbnails();
        if (gallery.Items.Count == 0) return;
        return;
#pragma warning disable CS0162
        int start = 0;
        try
        {
            if (!gallery.IsHandleCreated || gallery.IsDisposed || gallery.Items.Count == 0) return;
            start = gallery.TopItem?.Index ?? 0;
        }
        catch (InvalidOperationException) { start = 0; }
        catch (ArgumentOutOfRangeException) { start = 0; }
        var rows = gallery.Items.Cast<ListViewItem>().Skip(Math.Max(0, start - 4)).Take(28).ToList();
        foreach (var row in rows)
        {
            var item = (MediaItem)row.Tag!; if (loadedThumbs.Contains(item.Remote) || !loadingThumbs.Add(item.Remote)) continue;
            try
            {
            var bytes = await Task.Run(() => { using var ms = new MemoryStream(); mtpDevice!.DownloadThumbnail(item.Remote, ms); return ms.ToArray(); });
                if (gallery.IsDisposed || row.ListView != gallery) return;
                if (bytes.Length > 0 && bytes.Length < 16 * 1024 * 1024)
                {
                    using var ms = new MemoryStream(bytes); using var source = Image.FromStream(ms); var copy = CropThumbnail(source, images.ImageSize);
                    thumbCache[item.Remote] = copy; images.Images.Add(copy); row.ImageIndex = images.Images.Count - 1; loadedThumbs.Add(item.Remote);
                    gallery.BeginUpdate(); gallery.Invalidate(row.Bounds); gallery.EndUpdate();
                }
            }
            catch (Exception ex) { Log("Thumbnail " + item.Remote, ex); }
            finally { loadingThumbs.Remove(item.Remote); }
        }
    }
#pragma warning restore CS0162

    static Bitmap CropThumbnail(Image source, Size target)
    {
        var result = new Bitmap(target.Width, target.Height);
        using var g = Graphics.FromImage(result); g.Clear(Color.FromArgb(235, 235, 246));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        var scale = Math.Max((double)target.Width / source.Width, (double)target.Height / source.Height);
        var w = (int)Math.Ceiling(source.Width * scale); var h = (int)Math.Ceiling(source.Height * scale);
        var x = (target.Width - w) / 2; var y = (target.Height - h) / 2;
        g.DrawImage(source, new Rectangle(x, y, w, h)); return result;
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
        try { await Adb.Run("-s", serial!, "shell", "am", "start-foreground-service", "-n", "com.photoarchive.app/com.example.saftest.ThumbnailBridgeService"); } catch { return false; }
        for (var i = 0; i < 12; i++)
        {
            try { using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", 18765); return true; }
            catch { await Task.Delay(100); }
        }
        return false;
    }

    static async Task<byte[]> BridgeThumbnail(string path)
    {
        using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1", 18765);
        var stream = client.GetStream();
        var request = Encoding.UTF8.GetBytes(path);
        if (request.Length > ushort.MaxValue) return Array.Empty<byte>();
        await stream.WriteAsync(new[] { (byte)(request.Length >> 8), (byte)request.Length });
        await stream.WriteAsync(request); await stream.FlushAsync();
        var header = await ReadExactly(stream, 4); if (header.Length != 4) return Array.Empty<byte>();
        var length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
        if (length <= 0 || length > 2_000_000) return Array.Empty<byte>();
        return await ReadExactly(stream, length);
    }

    static async Task<byte[]> ReadExactly(NetworkStream stream, int count)
    {
        var data = new byte[count]; var offset = 0;
        while (offset < count) { var read = await stream.ReadAsync(data.AsMemory(offset, count - offset)); if (read == 0) break; offset += read; }
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

    void SelectAllVisible() { foreach (ListViewItem row in gallery.Items) row.Selected = true; UpdateSelected(); }
    void UpdateSelected() => selected.Text = $"Выбрано: {chosen.Count:N0} файлов";
    static void Log(string where, Exception ex) => Debug.WriteLine($"[PhotoArchive] {where}: {ex}");
}

sealed class BufferedListView : ListView
{
    public BufferedListView() { DoubleBuffered = true; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
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
    public BrandMark() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
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
        e.Graphics.FillRoundedRectangle(background, box, AppTheme.RadiusMedium);
        using var pen = new Pen(Color.White, 2.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        // A deliberately simple closed cloud contour. It does not depend on
        // an installed icon font and cannot self-intersect at small sizes.
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
    public ChromeButton(ChromeButtonKind kind) { this.kind = kind; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(105, 108, 126), 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var cx = Width / 2; var cy = Height / 2;
        if (kind == ChromeButtonKind.Minimize) e.Graphics.DrawLine(pen, cx - 7, cy + 3, cx + 7, cy + 3);
        else if (kind == ChromeButtonKind.Maximize) e.Graphics.DrawRectangle(pen, cx - 5, cy - 5, 10, 10);
        else { e.Graphics.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5); e.Graphics.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5); }
    }
}

sealed class DesignedProgressBar : Control
{
    int maximum = 1, value;
    bool indeterminate;
    readonly System.Windows.Forms.Timer animation = new() { Interval = 35 };
    int offset;
    public int Maximum { get => maximum; set { maximum = Math.Max(1, value); this.value = Math.Min(this.value, maximum); Invalidate(); } }
    public int Value { get => value; set { this.value = Math.Clamp(value, 0, maximum); Invalidate(); } }
    public bool Indeterminate { get => indeterminate; set { indeterminate = value; animation.Enabled = value; Invalidate(); } }
    public DesignedProgressBar()
    {
        Height = 14; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        animation.Tick += (_, _) => { offset = (offset + 8) % 180; Invalidate(); };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 2, Math.Max(1, Width - 1), Math.Max(8, Height - 4));
        using var empty = new SolidBrush(Color.FromArgb(232, 230, 244));
        e.Graphics.FillRoundedRectangle(empty, track, track.Height);
        Rectangle fill;
        if (Indeterminate) fill = new Rectangle(offset - 90, 2, 90, track.Height);
        else fill = new Rectangle(0, 2, (int)(track.Width * (value / (double)Math.Max(1, maximum))), track.Height);
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
        Text = "ФотоАрхив — подготовка медиатеки"; FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; ControlBox = false;
        ClientSize = new Size(460, 150); BackColor = Color.White; Font = new Font("Segoe UI", 10f);
        title.Text = "Сканируем телефон"; title.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
        title.Location = new Point(24, 18); title.AutoSize = true;
        details.Text = "Подготавливаем список файлов…"; details.ForeColor = Color.FromArgb(82, 79, 120);
        details.Location = new Point(24, 53); details.AutoSize = true;
        bar.Location = new Point(24, 91); bar.Size = new Size(412, 16);
        Controls.AddRange(new Control[] { title, details, bar });
    }
    public void SetCounting(int count) { title.Text = "Сканируем телефон"; details.Text = $"Найдено файлов: {count:N0}"; bar.Indeterminate = false; bar.Maximum = Math.Max(1, count); bar.Value = count; }
    public void SetTotal(int total) { title.Text = "Получаем список файлов"; details.Text = $"Получено: 0 / {total:N0}"; bar.Indeterminate = false; bar.Maximum = Math.Max(1, total); bar.Value = 0; }
    public void SetReceived(int count) { if (bar.Maximum > 1) bar.Value = Math.Min(bar.Maximum, count); details.Text = $"Получено: {count:N0} / {bar.Maximum:N0}"; }
    public void SetPreview() { title.Text = "Список получен"; details.Text = "Загружаем превью файлов…"; bar.Value = bar.Maximum; }
}

sealed class RoundButton : Button
{
    public RoundButton(string text) { Text = text; AutoSize = false; Height = 38; Width = Math.Max(140, text.Length * 8 + 34); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.FromArgb(91, 104, 224); ForeColor = Color.White; Font = new Font("Segoe UI Semibold", 9.5f); Cursor = Cursors.Hand; Margin = new Padding(6, 4, 6, 4); }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var b = new SolidBrush(Enabled ? BackColor : Color.FromArgb(211, 211, 225)); e.Graphics.FillRoundedRectangle(b, ClientRectangle, 12); TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(120, 120, 135), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); }
}

static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush b, Rectangle r, int radius) { using var p = Rounded(r, radius); g.FillPath(b, p); }
    static GraphicsPath Rounded(Rectangle r, int d) { var p = new GraphicsPath(); p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
}

static class Adb
{
    static string Exe => new[] { @"C:\Android\Sdk\platform-tools\adb.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "adb.exe"), "adb.exe" }.FirstOrDefault(File.Exists) ?? "adb.exe";
    public static Task<string> Run(params string[] args) => Task.Run(() => Execute(args, false));
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
            using var p = Start(args, false); p.Start();
            while (await p.StandardOutput.ReadLineAsync() is { } line)
            {
                var value = line.Trim(); if (value.Length > 0) onLine(value);
            }
            var error = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync();
            if (p.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        });
    }
    static string Execute(string[] args, bool _) { using var p = Start(args, false); p.Start(); var output = p.StandardOutput.ReadToEnd(); var error = p.StandardError.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new InvalidOperationException(error.Trim()); return output; }
    static byte[] ExecuteBytes(string[] args) { using var p = Start(args, true); p.Start(); using var ms = new MemoryStream(); p.StandardOutput.BaseStream.CopyTo(ms); var error = p.StandardError.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new InvalidOperationException(error.Trim()); return ms.ToArray(); }
    static Process Start(string[] args, bool binary) { var psi = new ProcessStartInfo(Exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }; foreach (var a in args) psi.ArgumentList.Add(a); return Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить ADB"); }
}
