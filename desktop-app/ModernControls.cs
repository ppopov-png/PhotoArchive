using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace PhotoArchive;

internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = AppTheme.RadiusLarge;
    public Color BorderColor { get; set; } = AppTheme.Border;
    public bool DrawBorder { get; set; } = true;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = CreatePath(rectangle, Radius);
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillPath(background, path);
        if (!DrawBorder) return;
        using var border = new Pen(BorderColor);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (Width <= 0 || Height <= 0) return;
        using var path = CreatePath(new Rectangle(0, 0, Width, Height), Radius);
        Region = new Region(path);
    }

    static GraphicsPath CreatePath(Rectangle rectangle, int radius)
    {
        var diameter = Math.Min(Math.Min(rectangle.Width, rectangle.Height), radius * 2);
        var path = new GraphicsPath();
        if (diameter <= 1)
        {
            path.AddRectangle(rectangle);
            return path;
        }
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class TransferProgressDialog : Form
{
    readonly CancellationTokenSource cancellation;
    readonly string destinationFolder;

    readonly Label title = new();
    readonly Label subtitle = new();
    readonly Label percent = new();
    readonly Label count = new();
    readonly Label bytes = new();
    readonly Label fileValue = new();
    readonly Label speedValue = new();
    readonly Label etaValue = new();
    readonly Label errorsValue = new();
    readonly Label currentName = new();
    readonly Label currentPath = new();
    readonly Label phase1 = new();
    readonly Label phase2 = new();
    readonly Label phase3 = new();
    readonly Label phase4 = new();
    readonly DesignedProgressBar progress = new();
    readonly RoundButton backgroundButton = new("Продолжить в фоне");
    readonly RoundButton cancelButton = new("Отменить");

    bool terminal;
    TransferSnapshot? latest;

    public TransferProgressDialog(int files, string folder, CancellationTokenSource cancellation)
    {
        this.cancellation = cancellation;
        destinationFolder = folder;

        Text = "ФотоАрхив — сортировка";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 550);
        BackColor = AppTheme.Surface;
        Font = AppTheme.BodyFont();
        Padding = new Padding(0);

        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint, true);

        var headerIcon = new BrandMark
        {
            Location = new Point(30, 28),
            Size = new Size(46, 46)
        };

        title.Text = "Сортировка выполняется";
        title.Font = AppTheme.TitleFont(18.5f);
        title.ForeColor = AppTheme.TextPrimary;
        title.Location = new Point(92, 27);
        title.Size = new Size(500, 32);

        subtitle.Text = "Копируем файлы с телефона и раскладываем по папкам по дате съёмки";
        subtitle.Font = AppTheme.BodyFont(9.8f);
        subtitle.ForeColor = AppTheme.TextSecondary;
        subtitle.Location = new Point(93, 61);
        subtitle.Size = new Size(520, 24);

        var minimize = CreateChromeLabel("—");
        minimize.Location = new Point(632, 18);
        minimize.Click += (_, _) => Hide();

        var close = CreateChromeLabel("×");
        close.Location = new Point(672, 18);
        close.Font = new Font("Segoe UI", 17f, FontStyle.Regular);
        close.Click += (_, _) =>
        {
            if (terminal) Close();
            else Hide();
        };

        progress.Location = new Point(30, 112);
        progress.Size = new Size(596, 16);
        progress.Maximum = 1000;
        progress.Value = 0;

        percent.Location = new Point(632, 99);
        percent.Size = new Size(58, 34);
        percent.Font = AppTheme.TitleFont(17f);
        percent.ForeColor = AppTheme.Primary;
        percent.TextAlign = ContentAlignment.MiddleRight;
        percent.Text = "0%";

        count.Location = new Point(30, 138);
        count.Size = new Size(350, 22);
        count.ForeColor = AppTheme.TextSecondary;
        count.Font = AppTheme.BodyFont(9.4f);
        count.Text = $"0 из {files:N0} файлов";

        bytes.Location = new Point(390, 138);
        bytes.Size = new Size(300, 22);
        bytes.TextAlign = ContentAlignment.MiddleRight;
        bytes.ForeColor = AppTheme.TextMuted;
        bytes.Font = AppTheme.BodyFont(9.1f);
        bytes.Text = "Подготавливаем объём…";

        Controls.AddRange(new Control[]
        {
            headerIcon, title, subtitle, minimize, close,
            progress, percent, count, bytes
        });

        Controls.Add(CreateStatCard(new Point(30, 177), "Файлов", fileValue, StatKind.Files));
        Controls.Add(CreateStatCard(new Point(198, 177), "Скорость", speedValue, StatKind.Speed));
        Controls.Add(CreateStatCard(new Point(366, 177), "Осталось", etaValue, StatKind.Time));
        Controls.Add(CreateStatCard(new Point(534, 177), "Ошибки", errorsValue, StatKind.Error));

        var currentCard = new RoundedPanel
        {
            Location = new Point(30, 260),
            Size = new Size(660, 86),
            BackColor = AppTheme.SurfaceSoft,
            BorderColor = AppTheme.Border,
            Radius = 14
        };

        var currentCaption = new Label
        {
            Text = "Обрабатывается",
            AutoSize = true,
            ForeColor = AppTheme.TextSecondary,
            Font = AppTheme.CaptionFont(),
            Location = new Point(16, 12)
        };

        currentName.Font = AppTheme.HeadingFont(10.4f);
        currentName.ForeColor = AppTheme.TextPrimary;
        currentName.Location = new Point(16, 34);
        currentName.Size = new Size(620, 22);
        currentName.AutoEllipsis = true;
        currentName.Text = "Подготавливаем список файлов";

        currentPath.ForeColor = AppTheme.TextSecondary;
        currentPath.Location = new Point(16, 59);
        currentPath.Size = new Size(620, 18);
        currentPath.AutoEllipsis = true;
        currentPath.Font = AppTheme.CaptionFont(8.6f);
        currentPath.Text = folder;

        currentCard.Controls.AddRange(new Control[] { currentCaption, currentName, currentPath });
        Controls.Add(currentCard);

        BuildPhaseTracker();

        backgroundButton.Location = new Point(282, 474);
        backgroundButton.Size = new Size(220, 48);
        backgroundButton.BackColor = AppTheme.Surface;
        backgroundButton.ForeColor = AppTheme.Primary;
        backgroundButton.FlatAppearance.BorderSize = 1;
        backgroundButton.FlatAppearance.BorderColor = AppTheme.BorderStrong;
        backgroundButton.Click += (_, _) => BackgroundAction();

        cancelButton.Location = new Point(516, 474);
        cancelButton.Size = new Size(174, 48);
        cancelButton.BackColor = AppTheme.DangerSoft;
        cancelButton.ForeColor = AppTheme.Danger;
        cancelButton.Click += (_, _) => CancelOrClose();

        Controls.AddRange(new Control[] { backgroundButton, cancelButton });

        FormClosing += (_, e) =>
        {
            if (terminal) return;
            e.Cancel = true;
            Hide();
        };

        Resize += (_, _) => ApplyRoundedRegion();
        ApplyRoundedRegion();
    }

    Label CreateChromeLabel(string text) => new()
    {
        Text = text,
        Size = new Size(36, 32),
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = AppTheme.TextSecondary,
        Font = AppTheme.BodyFont(12f),
        Cursor = Cursors.Hand
    };

    enum StatKind { Files, Speed, Time, Error }

    RoundedPanel CreateStatCard(Point location, string caption, Label value, StatKind kind)
    {
        var card = new RoundedPanel
        {
            Location = location,
            Size = new Size(156, 68),
            Radius = 12,
            BackColor = AppTheme.Surface,
            BorderColor = AppTheme.Border
        };

        var glyph = new StatGlyph
        {
            Kind = kind,
            Location = new Point(12, 18),
            Size = new Size(28, 28)
        };

        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = true,
            Font = AppTheme.CaptionFont(8.4f),
            ForeColor = AppTheme.TextSecondary,
            Location = new Point(48, 10)
        };

        value.Text = caption == "Файлов" ? "0 / 0" : caption == "Ошибки" ? "0" : "—";
        value.Font = AppTheme.HeadingFont(10.2f);
        value.ForeColor = AppTheme.TextPrimary;
        value.Location = new Point(48, 31);
        value.Size = new Size(98, 24);

        card.Controls.AddRange(new Control[] { glyph, captionLabel, value });
        return card;
    }

    void BuildPhaseTracker()
    {
        var y = 374;
        var xs = new[] { 58, 225, 392, 559 };

        using var linePen = new Pen(AppTheme.BorderStrong, 2f);
        var line = new PhaseLine
        {
            Location = new Point(xs[0] + 18, y + 13),
            Size = new Size(xs[3] - xs[0] - 2, 2)
        };
        Controls.Add(line);

        phase1.SetBounds(xs[0] - 28, y + 34, 120, 42);
        phase2.SetBounds(xs[1] - 34, y + 34, 132, 42);
        phase3.SetBounds(xs[2] - 42, y + 34, 148, 42);
        phase4.SetBounds(xs[3] - 35, y + 34, 128, 42);

        phase1.Text = "1. Сканирование";
        phase2.Text = "2. Копирование";
        phase3.Text = "3. Раскладка\nпо папкам";
        phase4.Text = "4. Проверка";

        foreach (var label in new[] { phase1, phase2, phase3, phase4 })
        {
            label.TextAlign = ContentAlignment.TopCenter;
            label.Font = AppTheme.CaptionFont(8.6f);
            label.ForeColor = AppTheme.TextMuted;
            label.BackColor = Color.Transparent;
            Controls.Add(label);
        }

        Controls.Add(new PhaseDot { Name = "phaseDot1", Location = new Point(xs[0], y), Size = new Size(28, 28) });
        Controls.Add(new PhaseDot { Name = "phaseDot2", Location = new Point(xs[1], y), Size = new Size(28, 28) });
        Controls.Add(new PhaseDot { Name = "phaseDot3", Location = new Point(xs[2], y), Size = new Size(28, 28) });
        Controls.Add(new PhaseDot { Name = "phaseDot4", Location = new Point(xs[3], y), Size = new Size(28, 28) });

        UpdatePhases(TransferStage.Preparing);
    }

    public void Apply(TransferSnapshot snapshot)
    {
        if (IsDisposed) return;
        latest = snapshot;

        var value = (int)Math.Round(snapshot.Fraction * 1000);
        progress.Value = Math.Clamp(value, 0, 1000);
        percent.Text = $"{snapshot.Fraction:P0}";
        count.Text = $"{snapshot.CompletedFiles:N0} из {snapshot.TotalFiles:N0} файлов";
        fileValue.Text = $"{snapshot.CompletedFiles:N0} / {snapshot.TotalFiles:N0}";
        speedValue.Text = FormatSpeed(snapshot.BytesPerSecond);
        etaValue.Text = snapshot.Remaining is { } remaining ? FormatDuration(remaining) : "Вычисляем…";
        errorsValue.Text = snapshot.Errors.ToString("N0");
        errorsValue.ForeColor = snapshot.Errors == 0 ? AppTheme.TextPrimary : AppTheme.Danger;
        currentName.Text = string.IsNullOrWhiteSpace(snapshot.CurrentFile) ? "—" : snapshot.CurrentFile;
        currentPath.Text = string.IsNullOrWhiteSpace(snapshot.CurrentFolder) ? destinationFolder : snapshot.CurrentFolder;
        bytes.Text = snapshot.TotalBytes > 0
            ? $"{FormatBytes(snapshot.CompletedBytes)} из {FormatBytes(snapshot.TotalBytes)}"
            : "Подготавливаем объём…";

        UpdatePhases(snapshot.Stage);

        switch (snapshot.Stage)
        {
            case TransferStage.Preparing:
                title.Text = "Подготовка файлов";
                subtitle.Text = "Определяем размеры и создаём очередь копирования";
                break;
            case TransferStage.Copying:
                title.Text = "Сортировка выполняется";
                subtitle.Text = "Копируем файлы с телефона и раскладываем по папкам по дате съёмки";
                break;
            case TransferStage.Organizing:
                title.Text = "Раскладываем файлы";
                subtitle.Text = "Формируем аккуратную структуру архива по датам";
                break;
            case TransferStage.Verifying:
                title.Text = "Проверяем результат";
                subtitle.Text = "Финальная проверка скопированных файлов";
                break;
            case TransferStage.Completed:
                ShowCompleted(snapshot);
                break;
        }
    }

    void ShowCompleted(TransferSnapshot snapshot)
    {
        terminal = true;
        progress.Value = 1000;
        percent.Text = "100%";
        etaValue.Text = "Готово";
        title.Text = snapshot.Errors == 0 ? "Сортировка завершена" : "Сортировка завершена с ошибками";
        subtitle.Text = snapshot.Errors == 0
            ? "Все выбранные файлы скопированы и разложены по датам"
            : $"Готово. Файлов с ошибками: {snapshot.Errors:N0}";
        currentName.Text = snapshot.Errors == 0 ? "Архив готов" : "Архив создан, часть файлов пропущена";
        currentPath.Text = destinationFolder;
        backgroundButton.Text = "Открыть папку";
        cancelButton.Text = "Готово";
        UpdatePhases(TransferStage.Completed);
    }

    public void ShowCancelled()
    {
        terminal = true;
        title.Text = "Сортировка отменена";
        subtitle.Text = "Уже скопированные файлы сохранены в папке назначения";
        cancelButton.Text = "Закрыть";
        backgroundButton.Text = "Открыть папку";
    }

    public void ShowFailure(string message)
    {
        terminal = true;
        title.Text = "Не удалось завершить сортировку";
        subtitle.Text = message;
        subtitle.ForeColor = AppTheme.Danger;
        cancelButton.Text = "Закрыть";
        backgroundButton.Text = "Открыть папку";
    }

    void BackgroundAction()
    {
        if (!terminal)
        {
            Hide();
            return;
        }

        var folder = latest?.CurrentFolder;
        if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder)) folder = destinationFolder;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    void CancelOrClose()
    {
        if (terminal)
        {
            Close();
            return;
        }

        using var confirmation = new CancelTransferDialog();
        if (confirmation.ShowDialog(this) == DialogResult.OK)
            cancellation.Cancel();
    }

    void UpdatePhases(TransferStage stage)
    {
        var current = stage switch
        {
            TransferStage.Preparing => 1,
            TransferStage.Copying => 2,
            TransferStage.Organizing => 3,
            TransferStage.Verifying => 4,
            TransferStage.Completed => 5,
            _ => 0
        };

        var labels = new[] { phase1, phase2, phase3, phase4 };
        for (var i = 0; i < labels.Length; i++)
            labels[i].ForeColor = i + 1 == current ? AppTheme.Primary : i + 1 < current ? AppTheme.TextSecondary : AppTheme.TextMuted;

        for (var i = 1; i <= 4; i++)
        {
            if (Controls[$"phaseDot{i}"] is not PhaseDot dot) continue;
            dot.State = i < current ? PhaseDotState.Completed : i == current ? PhaseDotState.Current : PhaseDotState.Pending;
            if (stage == TransferStage.Completed) dot.State = PhaseDotState.Completed;
            dot.Invalidate();
        }
    }

    void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = new GraphicsPath();
        const int d = 30;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(Width - d - 1, 0, d, d, 270, 90);
        path.AddArc(Width - d - 1, Height - d - 1, d, d, 0, 90);
        path.AddArc(0, Height - d - 1, d, d, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    static string FormatSpeed(double value)
    {
        if (value <= 0) return "—";
        return value >= 1024 * 1024
            ? $"{value / 1024 / 1024:0.0} МБ/с"
            : $"{value / 1024:0} КБ/с";
    }

    static string FormatDuration(TimeSpan value)
    {
        if (value.TotalSeconds < 1) return "< 1 с";
        if (value.TotalHours >= 1) return $"~ {value:h\\:mm\\:ss}";
        if (value.TotalMinutes >= 1) return $"~ {(int)value.TotalMinutes} мин {value.Seconds} с";
        return $"~ {value.Seconds} с";
    }

    static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024 * 1024) return $"{value / 1024d / 1024 / 1024:0.0} ГБ";
        if (value >= 1024L * 1024) return $"{value / 1024d / 1024:0.0} МБ";
        if (value >= 1024L) return $"{value / 1024d:0} КБ";
        return $"{value} Б";
    }

    sealed class PhaseLine : Control
    {
        public PhaseLine() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            using var pen = new Pen(AppTheme.BorderStrong, 2f);
            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
        }
    }

    enum PhaseDotState { Pending, Current, Completed }

    sealed class PhaseDot : Control
    {
        public PhaseDotState State { get; set; } = PhaseDotState.Pending;
        public PhaseDot() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(2, 2, Width - 5, Height - 5);
            var fill = State == PhaseDotState.Pending ? AppTheme.Surface : AppTheme.Primary;
            var border = State == PhaseDotState.Pending ? AppTheme.BorderStrong : AppTheme.Primary;
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border, 2f);
            e.Graphics.FillEllipse(brush, rect);
            e.Graphics.DrawEllipse(pen, rect);

            if (State == PhaseDotState.Completed)
            {
                using var check = new Pen(Color.White, 2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                e.Graphics.DrawLines(check, new[]
                {
                    new Point(8, 14),
                    new Point(12, 18),
                    new Point(20, 10)
                });
            }
            else if (State == PhaseDotState.Current)
            {
                using var dot = new SolidBrush(Color.White);
                e.Graphics.FillEllipse(dot, 11, 11, 6, 6);
            }
        }
    }

    sealed class StatGlyph : Control
    {
        public StatKind Kind { get; set; }
        public StatGlyph() => SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Kind == StatKind.Error ? AppTheme.Danger : AppTheme.Primary, 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            switch (Kind)
            {
                case StatKind.Files:
                    e.Graphics.DrawRectangle(pen, 7, 5, 13, 17);
                    e.Graphics.DrawLine(pen, 10, 10, 17, 10);
                    e.Graphics.DrawLine(pen, 10, 14, 17, 14);
                    break;
                case StatKind.Speed:
                    e.Graphics.DrawArc(pen, 4, 5, 20, 20, 190, 160);
                    e.Graphics.DrawLine(pen, 14, 15, 20, 9);
                    break;
                case StatKind.Time:
                    e.Graphics.DrawEllipse(pen, 4, 4, 20, 20);
                    e.Graphics.DrawLine(pen, 14, 14, 14, 8);
                    e.Graphics.DrawLine(pen, 14, 14, 19, 16);
                    break;
                case StatKind.Error:
                    e.Graphics.DrawPolygon(pen, new[] { new Point(14, 3), new Point(25, 23), new Point(3, 23) });
                    e.Graphics.DrawLine(pen, 14, 9, 14, 16);
                    e.Graphics.FillEllipse(new SolidBrush(AppTheme.Danger), 13, 19, 2, 2);
                    break;
            }
        }
    }
}

internal sealed class CancelTransferDialog : Form
{
    public CancelTransferDialog()
    {
        Text = "Остановить сортировку";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 208);
        BackColor = AppTheme.Surface;
        Font = AppTheme.BodyFont();

        var title = new Label
        {
            Text = "Остановить сортировку?",
            Font = AppTheme.HeadingFont(14f),
            ForeColor = AppTheme.TextPrimary,
            Location = new Point(24, 22),
            Size = new Size(390, 30)
        };

        var text = new Label
        {
            Text = "Уже скопированные файлы останутся в папке назначения. Текущий незавершённый файл будет удалён.",
            Font = AppTheme.BodyFont(9.4f),
            ForeColor = AppTheme.TextSecondary,
            Location = new Point(24, 60),
            Size = new Size(390, 54)
        };

        var continueButton = new RoundButton("Продолжить")
        {
            Location = new Point(112, 136),
            Size = new Size(140, 44),
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextPrimary,
            DialogResult = DialogResult.Cancel
        };

        var stopButton = new RoundButton("Остановить")
        {
            Location = new Point(266, 136),
            Size = new Size(148, 44),
            BackColor = AppTheme.DangerSoft,
            ForeColor = AppTheme.Danger,
            DialogResult = DialogResult.OK
        };

        Controls.AddRange(new Control[] { title, text, continueButton, stopButton });
        AcceptButton = stopButton;
        CancelButton = continueButton;
    }
}
