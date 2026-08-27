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
        form.MinimumSize = new Size(1120, 740);
        if (form.Width < 1280 || form.Height < 780) form.Size = new Size(1380, 900);

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
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        form.Controls.Add(root);

        var header = BuildHeader(form, hiddenDevices, out var deviceView, out var refreshButton, out var mediaButton, out var selectAllButton);
        root.Controls.Add(header, 0, 0);

        var mediaShell = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 18,
            DrawBorder = true,
            BorderColor = AppTheme.Border,
            BackColor = AppTheme.Surface,
            Margin = new Padding(0, 0, 0, 10)
        };

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = AppTheme.Surface };
        toolbar.Paint += (_, args) =>
        {
            using var pen = new Pen(AppTheme.Border);
            args.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
        };

        var tabs = new FlowLayoutPanel
        {
            Location = new Point(16, 12),
            Size = new Size(700, 42),
            BackColor = AppTheme.Surface,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };
        var allTab = Tab("Все файлы", 110, true);
        var photoTab = Tab("Фото", 88, false);
        var videoTab = Tab("Видео", 88, false);
        var period = Secondary("▣  За всё время  ⌄", 168);
        var filters = Secondary("☷  Фильтры", 126);
        tabs.Controls.AddRange(new Control[] { allTab, photoTab, videoTab, period, filters });

        var selectedLabel = new Label
        {
            Text = "Выбрано: 0 файлов",
            AutoSize = false,
            Size = new Size(230, 42),
            Font = AppTheme.ButtonFont(9.2f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        void PositionSelected() => selectedLabel.Location = new Point(toolbar.ClientSize.Width - selectedLabel.Width - 18, 12);
        toolbar.Resize += (_, _) => PositionSelected();
        PositionSelected();
        toolbar.Controls.Add(tabs);
        toolbar.Controls.Add(selectedLabel);

        var gallery = new MediaGalleryView
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ThumbnailProvider = remote => thumbCache.TryGetValue(remote, out var image) ? image : null
        };
        var galleryHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1, 0, 1, 1), BackColor = AppTheme.Surface };
        galleryHost.Controls.Add(gallery);
        mediaShell.Controls.Add(galleryHost);
        mediaShell.Controls.Add(toolbar);
        toolbar.BringToFront();
        root.Controls.Add(mediaShell, 0, 1);

        var footer = BuildFooter(form, hiddenDestination, out var pathView, out var chooseFolder, out var startButton);
        root.Controls.Add(footer, 0, 2);

        var statusBar = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };
        var connection = new Label
        {
            Dock = DockStyle.Left,
            Width = 500,
            Text = "●  Телефон не подключён",
            Font = AppTheme.CaptionFont(8.8f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var summary = new Label
        {
            Dock = DockStyle.Right,
            Width = 550,
            Text = "Всего файлов на телефоне: —",
            Font = AppTheme.CaptionFont(8.8f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleRight
        };
        statusBar.Controls.Add(connection);
        statusBar.Controls.Add(summary);
        root.Controls.Add(statusBar, 0, 3);

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

        selectAllButton.Click += (_, _) =>
        {
            gallery.SelectAll();
        };

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
            await InvokeTask(form, "RefreshDevices");
            SyncDevice(hiddenDevices, deviceView, connection);
        };

        var uiTimer = new System.Windows.Forms.Timer { Interval = 180 };
        uiTimer.Tick += (_, _) =>
        {
            if (form.IsDisposed) { uiTimer.Stop(); uiTimer.Dispose(); return; }
            gallery.Invalidate();
            if (allMedia.Count != galleryCountCache)
            {
                galleryCountCache = allMedia.Count;
                ApplyFilter();
            }
            SyncDevice(hiddenDevices, deviceView, connection);
        };
        var galleryCountCache = allMedia.Count;
        uiTimer.Start();

        SyncDevice(hiddenDevices, deviceView, connection);
        pathView.PathText = hiddenDestination.Text;
        SetTab("Все");
        form.ResumeLayout(true);
        form.Invalidate(true);
    }

    static RoundedPanel BuildHeader(MainForm form, ComboBox hiddenDevices, out DeviceSelectorView deviceView,
        out RoundButton refreshButton, out RoundButton mediaButton, out RoundButton selectAllButton)
    {
        var header = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 20,
            DrawBorder = true,
            BorderColor = AppTheme.Border,
            BackColor = AppTheme.Surface,
            Margin = new Padding(0, 0, 0, 12)
        };

        var logo = new BrandMark { Location = new Point(22, 18), Size = new Size(46, 46) };
        var title = new Label
        {
            Text = "ФотоАрхив",
            Location = new Point(80, 12),
            Size = new Size(360, 38),
            Font = AppTheme.TitleFont(23),
            ForeColor = AppTheme.TextPrimary
        };
        var subtitle = new Label
        {
            Text = "Сортировка фото и видео с телефона по датам",
            Location = new Point(81, 50),
            Size = new Size(430, 24),
            Font = AppTheme.BodyFont(10.1f),
            ForeColor = AppTheme.TextSecondary
        };

        deviceView = new DeviceSelectorView { Location = new Point(22, 91), Size = new Size(318, 46) };
        refreshButton = Primary("↻  Обновить телефон", 190);
        refreshButton.Location = new Point(354, 91);
        mediaButton = Secondary("▧  Медиатека", 166);
        mediaButton.Location = new Point(556, 91);
        selectAllButton = Secondary("☑  Выбрать всё", 166);
        selectAllButton.Location = new Point(734, 91);

        var help = new Label
        {
            Text = "ⓘ  Справка",
            AutoSize = false,
            Size = new Size(112, 34),
            Font = AppTheme.ButtonFont(9.2f),
            ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var minimize = new ChromeButton(ChromeButtonKind.Minimize) { Size = new Size(42, 34), Cursor = Cursors.Hand };
        var maximize = new ChromeButton(ChromeButtonKind.Maximize) { Size = new Size(42, 34), Cursor = Cursors.Hand };
        var close = new ChromeButton(ChromeButtonKind.Close) { Size = new Size(42, 34), Cursor = Cursors.Hand };
        minimize.Click += (_, _) => form.WindowState = FormWindowState.Minimized;
        maximize.Click += (_, _) => form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        close.Click += (_, _) => form.Close();

        void PositionRight()
        {
            close.Location = new Point(header.ClientSize.Width - 51, 7);
            maximize.Location = new Point(header.ClientSize.Width - 93, 7);
            minimize.Location = new Point(header.ClientSize.Width - 135, 7);
            help.Location = new Point(header.ClientSize.Width - 132, 97);
        }
        header.Resize += (_, _) => PositionRight();
        PositionRight();

        foreach (Control drag in new Control[] { header, logo, title, subtitle })
            drag.MouseDown += (_, args) =>
            {
                if (args.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(form.Handle, 0xA1, 0x2, 0);
            };

        header.Controls.AddRange(new Control[] { logo, title, subtitle, deviceView, refreshButton, mediaButton, selectAllButton, help, minimize, maximize, close });
        return header;
    }

    static RoundedPanel BuildFooter(MainForm form, TextBox hiddenDestination, out PathDisplayView pathView,
        out RoundButton chooseFolder, out RoundButton startButton)
    {
        var footer = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            Radius = 18,
            DrawBorder = true,
            BorderColor = AppTheme.Border,
            BackColor = AppTheme.Surface,
            Margin = new Padding(0, 0, 0, 8)
        };

        var iconShell = new RoundedPanel
        {
            Location = new Point(18, 24),
            Size = new Size(48, 48),
            Radius = 14,
            DrawBorder = false,
            BackColor = AppTheme.PrimarySoft
        };
        iconShell.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "▱",
            Font = new Font("Segoe Fluent Icons", 23f),
            ForeColor = AppTheme.Primary,
            TextAlign = ContentAlignment.MiddleCenter
        });

        var caption = new Label
        {
            Text = "Папка назначения",
            Location = new Point(80, 14),
            AutoSize = true,
            Font = AppTheme.CaptionFont(8.8f),
            ForeColor = AppTheme.TextSecondary
        };

        pathView = new PathDisplayView { Location = new Point(80, 37), Size = new Size(650, 44), Cursor = Cursors.Hand, PathText = hiddenDestination.Text };
        chooseFolder = Secondary("Изменить  ✎", 142);
        startButton = Primary("Начать сортировку", 218);
        startButton.Height = 52;
        startButton.Enabled = false;

        void Position()
        {
            var right = footer.ClientSize.Width - 18;
            startButton.Location = new Point(right - startButton.Width, 22);
            chooseFolder.Location = new Point(startButton.Left - chooseFolder.Width - 12, 27);
            pathView.Width = Math.Max(300, chooseFolder.Left - 92);
        }
        footer.Resize += (_, _) => Position();
        Position();
        footer.Controls.AddRange(new Control[] { iconShell, caption, pathView, chooseFolder, startButton });
        return footer;
    }

    static RoundButton Primary(string text, int width)
    {
        return new RoundButton(text)
        {
            Width = width,
            Height = 46,
            Radius = 12,
            BorderWidth = 0,
            BackColor = AppTheme.Primary,
            ForeColor = Color.White
        };
    }

    static RoundButton Secondary(string text, int width)
    {
        return new RoundButton(text)
        {
            Width = width,
            Height = 46,
            Radius = 12,
            BorderWidth = 1,
            BorderColor = AppTheme.BorderStrong,
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextPrimary
        };
    }

    static RoundButton Tab(string text, int width, bool active)
    {
        var button = new RoundButton(text)
        {
            Width = width,
            Height = 40,
            Radius = 10
        };
        SetTabVisual(button, active);
        return button;
    }

    static void SetTabVisual(RoundButton button, bool active)
    {
        button.BackColor = active ? AppTheme.PrimarySoft : AppTheme.Surface;
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
            connection.Text = "●  Телефон не подключён";
            connection.ForeColor = AppTheme.TextSecondary;
        }
        else
        {
            view.DisplayText = text.Contains("пользователь", StringComparison.OrdinalIgnoreCase) ? text : text;
            connection.Text = $"●  Телефон подключён  ·  {text}";
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
