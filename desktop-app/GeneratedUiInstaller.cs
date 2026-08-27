using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PhotoArchive;

internal static class GeneratedUiInstaller
{
    static bool installed;

    [ModuleInitializer]
    internal static void Register()
    {
        Application.Idle += InstallOnFirstIdle;
    }

    static void InstallOnFirstIdle(object? sender, EventArgs e)
    {
        if (installed) return;
        var form = Application.OpenForms.Cast<Form>().OfType<MainForm>().FirstOrDefault();
        if (form == null) return;
        installed = true;
        Application.Idle -= InstallOnFirstIdle;
        Install(form);
    }

    static void Install(MainForm form)
    {
        form.SuspendLayout();
        form.Controls.Clear();
        form.BackColor = AppTheme.Background;
        form.MinimumSize = new Size(1180, 760);
        if (form.Width < 1320 || form.Height < 820) form.Size = new Size(1420, 920);

        var hiddenDevices = Field<ComboBox>(form, "devices");
        var hiddenDestination = Field<TextBox>(form, "destination");
        var allMedia = Field<List<MediaItem>>(form, "allMedia");
        var chosen = Field<HashSet<string>>(form, "chosen");
        var thumbCache = Field<Dictionary<string, Image>>(form, "thumbCache");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            Padding = new Padding(12),
            RowCount = 4,
            ColumnCount = 1,
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 162));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        form.Controls.Add(root);

        var header = BuildHeader(form, out var deviceView, out var refreshButton, out var mediaButton, out var selectAllButton);
        root.Controls.Add(header, 0, 0);

        var mediaShell = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 18,
            DrawBorder = true,
            BorderColor = AppTheme.Border,
            BackColor = AppTheme.Surface,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(1)
        };

        var mediaGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        mediaGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        mediaGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = BuildToolbar(out var allTab, out var photoTab, out var videoTab, out var selectedLabel);
        mediaGrid.Controls.Add(toolbar, 0, 0);

        var gallery = new MediaGalleryView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ThumbnailProvider = remote => thumbCache.TryGetValue(remote, out var image) ? image : null
        };
        mediaGrid.Controls.Add(gallery, 0, 1);
        mediaShell.Controls.Add(mediaGrid);
        root.Controls.Add(mediaShell, 0, 1);

        var footer = BuildFooter(hiddenDestination, out var pathView, out var chooseFolder, out var startButton);
        root.Controls.Add(footer, 0, 2);

        var statusGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Background,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(8, 0, 8, 0)
        };
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var connection = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Телефон не подключён",
            Font = AppTheme.CaptionFont(8.8f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        var summary = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Всего файлов на телефоне: —",
            Font = AppTheme.CaptionFont(8.8f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0)
        };
        statusGrid.Controls.Add(connection, 0, 0);
        statusGrid.Controls.Add(summary, 1, 0);
        root.Controls.Add(statusGrid, 0, 3);

        string currentFilter = "Все";

        void ApplyFilter()
        {
            var visible = allMedia.Where(item => currentFilter == "Все" || (currentFilter == "Фото" && !item.Video) || (currentFilter == "Видео" && item.Video));
            gallery.SetItems(visible, chosen);
            selectedLabel.Text = $"Выбрано: {chosen.Count:N0} файлов";
            startButton.Enabled = chosen.Count > 0;
            summary.Text = allMedia.Count == 0
                ? "Всего файлов на телефоне: —"
                : $"Всего файлов на телефоне: {allMedia.Count:N0}  ·  Последнее обновление: {DateTime.Now:HH:mm}";
        }

        void SetTab(string value)
        {
            currentFilter = value;
            SetTabVisual(allTab, value == "Все");
            SetTabVisual(photoTab, value == "Фото");
            SetTabVisual(videoTab, value == "Видео");
            ApplyFilter();
        }

        allTab.Click += (_, _) => SetTab("Все");
        photoTab.Click += (_, _) => SetTab("Фото");
        videoTab.Click += (_, _) => SetTab("Видео");

        gallery.SelectionChanged += (_, _) =>
        {
            chosen.Clear();
            foreach (var remote in gallery.SelectedPaths) chosen.Add(remote);
            selectedLabel.Text = $"Выбрано: {chosen.Count:N0} файлов";
            startButton.Enabled = chosen.Count > 0;
        };

        selectAllButton.Click += (_, _) => gallery.SelectAll();

        refreshButton.Click += async (_, _) =>
        {
            refreshButton.Enabled = false;
            try
            {
                await InvokeTask(form, "RefreshDevices");
                SyncDevice(hiddenDevices, deviceView, connection);
            }
            finally { refreshButton.Enabled = true; }
        };

        mediaButton.Click += async (_, _) =>
        {
            mediaButton.Enabled = false;
            try
            {
                await InvokeTask(form, "LoadMedia");
                SyncDevice(hiddenDevices, deviceView, connection);
                ApplyFilter();
                gallery.Invalidate();
            }
            finally { mediaButton.Enabled = true; }
        };

        startButton.Click += async (_, _) =>
        {
            startButton.Enabled = false;
            try { await InvokeTask(form, "StartTransferAsync"); }
            finally { startButton.Enabled = chosen.Count > 0; }
        };

        chooseFolder.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Куда сохранить фотоархив?",
                SelectedPath = hiddenDestination.Text
            };
            if (dialog.ShowDialog(form) != DialogResult.OK) return;
            hiddenDestination.Text = dialog.SelectedPath;
            pathView.PathText = dialog.SelectedPath;
        };
        pathView.Click += (_, _) => chooseFolder.PerformClick();

        deviceView.Click += async (_, _) =>
        {
            refreshButton.Enabled = false;
            try
            {
                await InvokeTask(form, "RefreshDevices");
                SyncDevice(hiddenDevices, deviceView, connection);
            }
            finally { refreshButton.Enabled = true; }
        };

        var galleryCountCache = allMedia.Count;
        var uiTimer = new System.Windows.Forms.Timer { Interval = 220 };
        uiTimer.Tick += (_, _) =>
        {
            if (form.IsDisposed)
            {
                uiTimer.Stop();
                uiTimer.Dispose();
                return;
            }
            gallery.Invalidate();
            if (allMedia.Count != galleryCountCache)
            {
                galleryCountCache = allMedia.Count;
                ApplyFilter();
            }
            SyncDevice(hiddenDevices, deviceView, connection);
        };
        uiTimer.Start();

        SyncDevice(hiddenDevices, deviceView, connection);
        pathView.PathText = hiddenDestination.Text;
        SetTab("Все");
        form.ResumeLayout(true);
        form.Invalidate(true);
    }

    static RoundedPanel BuildHeader(MainForm form, out DeviceSelectorView deviceView,
        out UiButton refreshButton, out UiButton mediaButton, out UiButton selectAllButton)
    {
        var header = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 20,
            DrawBorder = true,
            BorderColor = AppTheme.Border,
            BackColor = AppTheme.Surface,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(18, 12, 18, 12)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppTheme.Surface
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));

        var logo = new BrandMark { Dock = DockStyle.Fill, Margin = new Padding(0, 5, 10, 8) };
        top.Controls.Add(logo, 0, 0);

        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppTheme.Surface
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label
        {
            Text = "ФотоАрхив",
            Dock = DockStyle.Fill,
            Font = AppTheme.TitleFont(23),
            ForeColor = AppTheme.TextPrimary,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0)
        };
        var subtitle = new Label
        {
            Text = "Сортировка фото и видео с телефона по датам",
            Dock = DockStyle.Fill,
            Font = AppTheme.BodyFont(10.1f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0)
        };
        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(subtitle, 0, 1);
        top.Controls.Add(titleStack, 1, 0);

        var chrome = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppTheme.Surface
        };
        chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        var minimize = new ChromeButton(ChromeButtonKind.Minimize) { Dock = DockStyle.Fill, Cursor = Cursors.Hand, Margin = new Padding(1, 3, 1, 28) };
        var maximize = new ChromeButton(ChromeButtonKind.Maximize) { Dock = DockStyle.Fill, Cursor = Cursors.Hand, Margin = new Padding(1, 3, 1, 28) };
        var close = new ChromeButton(ChromeButtonKind.Close) { Dock = DockStyle.Fill, Cursor = Cursors.Hand, Margin = new Padding(1, 3, 1, 28) };
        minimize.Click += (_, _) => form.WindowState = FormWindowState.Minimized;
        maximize.Click += (_, _) => form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        close.Click += (_, _) => form.Close();
        chrome.Controls.Add(minimize, 0, 0);
        chrome.Controls.Add(maximize, 1, 0);
        chrome.Controls.Add(close, 2, 0);
        top.Controls.Add(chrome, 2, 0);
        grid.Controls.Add(top, 0, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = AppTheme.Surface
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));

        var localDeviceView = new DeviceSelectorView { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 10, 2) };
        var localRefreshButton = Primary("Обновить телефон", UiIcon.Refresh);
        localRefreshButton.Margin = new Padding(4, 2, 4, 2);
        var localMediaButton = Secondary("Медиатека", UiIcon.Gallery);
        localMediaButton.Margin = new Padding(4, 2, 4, 2);
        var localSelectAllButton = Secondary("Выбрать всё", UiIcon.CheckSquare);
        localSelectAllButton.Margin = new Padding(4, 2, 4, 2);
        var help = Ghost("Справка", UiIcon.Help);
        help.Margin = new Padding(6, 2, 0, 2);

        actions.Controls.Add(localDeviceView, 0, 0);
        actions.Controls.Add(localRefreshButton, 1, 0);
        actions.Controls.Add(localMediaButton, 2, 0);
        actions.Controls.Add(localSelectAllButton, 3, 0);
        actions.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface }, 4, 0);
        actions.Controls.Add(help, 5, 0);
        grid.Controls.Add(actions, 0, 1);
        header.Controls.Add(grid);

        foreach (Control drag in new Control[] { header, top, titleStack, title, subtitle, logo })
            drag.MouseDown += (_, args) =>
            {
                if (args.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(form.Handle, 0xA1, 0x2, 0);
            };

        deviceView = localDeviceView;
        refreshButton = localRefreshButton;
        mediaButton = localMediaButton;
        selectAllButton = localSelectAllButton;
        return header;
    }

    static Control BuildToolbar(out UiButton allTab, out UiButton photoTab, out UiButton videoTab, out Label selectedLabel)
    {
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            ColumnCount = 7,
            RowCount = 1,
            Padding = new Padding(15, 11, 15, 11),
            Margin = new Padding(0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228));
        toolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
        };

        var localAll = Tab("Все файлы", true);
        var localPhoto = Tab("Фото", false);
        var localVideo = Tab("Видео", false);
        var period = Secondary("За всё время", UiIcon.Calendar);
        var filters = Secondary("Фильтры", UiIcon.Filter);

        toolbar.Controls.Add(localAll, 0, 0);
        toolbar.Controls.Add(localPhoto, 1, 0);
        toolbar.Controls.Add(localVideo, 2, 0);
        toolbar.Controls.Add(period, 3, 0);
        toolbar.Controls.Add(filters, 4, 0);
        toolbar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Surface }, 5, 0);

        var localSelected = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Выбрано: 0 файлов",
            Font = AppTheme.ButtonFont(9.2f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0)
        };
        toolbar.Controls.Add(localSelected, 6, 0);

        allTab = localAll;
        photoTab = localPhoto;
        videoTab = localVideo;
        selectedLabel = localSelected;
        return toolbar;
    }

    static RoundedPanel BuildFooter(TextBox hiddenDestination, out PathDisplayView pathView,
        out UiButton chooseFolder, out UiButton startButton)
    {
        var footer = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 18,
            DrawBorder = true,
            BorderColor = AppTheme.Border,
            BackColor = AppTheme.Surface,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(16, 13, 16, 13)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));

        var folder = new VectorIconView
        {
            Dock = DockStyle.Fill,
            Icon = UiIcon.Folder,
            IconPadding = 13,
            Margin = new Padding(0, 8, 8, 8)
        };
        grid.Controls.Add(folder, 0, 0);

        var pathStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = AppTheme.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(0)
        };
        pathStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        pathStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Папка назначения",
            Font = AppTheme.CaptionFont(8.8f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(3, 0, 0, 0)
        };
        var localPathView = new PathDisplayView
        {
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
            PathText = hiddenDestination.Text,
            Margin = new Padding(0, 2, 0, 0)
        };
        pathStack.Controls.Add(caption, 0, 0);
        pathStack.Controls.Add(localPathView, 0, 1);
        grid.Controls.Add(pathStack, 1, 0);

        var localChoose = Secondary("Изменить", UiIcon.Edit);
        localChoose.Margin = new Padding(5, 13, 5, 13);
        var localStart = Primary("Начать сортировку", UiIcon.None);
        localStart.Margin = new Padding(7, 8, 0, 8);
        localStart.Enabled = false;
        grid.Controls.Add(localChoose, 2, 0);
        grid.Controls.Add(localStart, 3, 0);

        footer.Controls.Add(grid);
        pathView = localPathView;
        chooseFolder = localChoose;
        startButton = localStart;
        return footer;
    }

    static UiButton Primary(string text, UiIcon icon)
    {
        return new UiButton
        {
            Dock = DockStyle.Fill,
            Text = text,
            Icon = icon,
            Radius = 12,
            BorderWidth = 0,
            BackColor = AppTheme.Primary,
            HoverBackColor = AppTheme.PrimaryHover,
            PressedBackColor = AppTheme.PrimaryPressed,
            ForeColor = Color.White
        };
    }

    static UiButton Secondary(string text, UiIcon icon)
    {
        return new UiButton
        {
            Dock = DockStyle.Fill,
            Text = text,
            Icon = icon,
            Radius = 12,
            BorderWidth = 1,
            BorderColor = AppTheme.BorderStrong,
            BackColor = AppTheme.Surface,
            HoverBackColor = AppTheme.SurfaceSoft,
            PressedBackColor = AppTheme.PrimarySoft,
            ForeColor = AppTheme.TextPrimary,
            Margin = new Padding(4, 0, 4, 0)
        };
    }

    static UiButton Ghost(string text, UiIcon icon)
    {
        return new UiButton
        {
            Dock = DockStyle.Fill,
            Text = text,
            Icon = icon,
            Radius = 10,
            BorderWidth = 0,
            BackColor = AppTheme.Surface,
            HoverBackColor = AppTheme.SurfaceSoft,
            PressedBackColor = AppTheme.PrimarySoft,
            ForeColor = AppTheme.TextSecondary
        };
    }

    static UiButton Tab(string text, bool active)
    {
        var button = new UiButton
        {
            Dock = DockStyle.Fill,
            Text = text,
            Radius = 10,
            Icon = UiIcon.None,
            Margin = new Padding(2, 0, 2, 0)
        };
        SetTabVisual(button, active);
        return button;
    }

    static void SetTabVisual(UiButton button, bool active)
    {
        button.BackColor = active ? AppTheme.PrimarySoft : AppTheme.Surface;
        button.HoverBackColor = active ? AppTheme.PrimarySoftHover : AppTheme.SurfaceSoft;
        button.PressedBackColor = AppTheme.PrimarySoftHover;
        button.ForeColor = active ? AppTheme.Primary : AppTheme.TextSecondary;
        button.BorderWidth = active ? 1 : 0;
        button.BorderColor = active ? Color.FromArgb(211, 215, 255) : Color.Transparent;
        button.Invalidate();
    }

    static void SyncDevice(ComboBox hiddenDevices, DeviceSelectorView view, Label connection)
    {
        var text = hiddenDevices.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            view.DisplayText = "Телефон не подключён";
            connection.Text = "Телефон не подключён";
            connection.ForeColor = AppTheme.TextSecondary;
        }
        else
        {
            view.DisplayText = text;
            connection.Text = $"Телефон подключён  ·  {text}";
            connection.ForeColor = AppTheme.Success;
        }
    }

    static async Task InvokeTask(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, methodName);
        var result = method.Invoke(target, null);
        if (result is Task task) await task;
    }

    static T Field<T>(object target, string name) where T : class
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(target.GetType().Name, name);
        return (T)(field.GetValue(target) ?? throw new InvalidOperationException($"Поле {name} не инициализировано"));
    }

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);
}
