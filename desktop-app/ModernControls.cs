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
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = CreatePath(rectangle, Radius);
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillPath(background, path);
        if (DrawBorder)
        {
            using var border = new Pen(BorderColor);
            e.Graphics.DrawPath(border, path);
        }
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
        if (diameter <= 1) { path.AddRectangle(rectangle); return path; }
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
    readonly Label title = new();
    readonly Label subtitle = new();
    readonly Label percent = new();
    readonly Label count = new();
    readonly Label fileValue = new();
    readonly Label speedValue = new();
    readonly Label etaValue = new();
    readonly Label errorsValue = new();
    readonly Label currentName = new();
    readonly Label currentPath = new();
    readonly Label stageText = new();
    readonly DesignedProgressBar progress = new();
    readonly RoundButton backgroundButton = new("▣  Продолжить в фоне");
    readonly RoundButton cancelButton = new("Отменить");
    bool terminal;

    public TransferProgressDialog(int files, string folder, CancellationTokenSource cancellation)
    {
        this.cancellation = cancellation;
        Text = "ФотоАрхив — сортировка";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(680, 520);
        BackColor = AppTheme.Surface;
        Font = AppTheme.BodyFont();
        Padding = new Padding(28);

        title.Text = "Сортировка выполняется";
        title.Font = AppTheme.TitleFont(19);
        title.ForeColor = AppTheme.TextPrimary;
        title.Location = new Point(86, 30);
        title.AutoSize = true;
        subtitle.Text = "Копируем файлы с телефона и раскладываем по папкам по дате съёмки";
        subtitle.Font = AppTheme.BodyFont(10);
        subtitle.ForeColor = AppTheme.TextSecondary;
        subtitle.Location = new Point(88, 65);
        subtitle.AutoSize = true;
        var processIcon = new BrandMark { Location = new Point(30, 26), Size = new Size(44, 44) };

        progress.Location = new Point(30, 108); progress.Size = new Size(552, 15); progress.Maximum = 1000;
        percent.Location = new Point(595, 96); percent.Size = new Size(60, 32); percent.Font = AppTheme.TitleFont(17); percent.ForeColor = AppTheme.Primary; percent.TextAlign = ContentAlignment.MiddleRight; percent.Text = "0%";
        count.Location = new Point(30, 132); count.Size = new Size(400, 24); count.ForeColor = AppTheme.TextSecondary; count.Text = $"0 из {files:N0} файлов";

        Controls.AddRange(new Control[] { processIcon, title, subtitle, progress, percent, count });
        Controls.Add(CreateStatCard(new Point(30, 170), "▤", "Файлов", fileValue));
        Controls.Add(CreateStatCard(new Point(190, 170), "◴", "Скорость", speedValue));
        Controls.Add(CreateStatCard(new Point(350, 170), "◷", "Осталось", etaValue));
        Controls.Add(CreateStatCard(new Point(510, 170), "⚠", "Ошибки", errorsValue));

        var currentCard = new RoundedPanel { Location = new Point(30, 258), Size = new Size(620, 82), BackColor = AppTheme.SurfaceSoft, Radius = 14 };
        var currentCaption = new Label { Text = "Обрабатывается", AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.BodyFont(9), Location = new Point(16, 12) };
        currentName.Font = AppTheme.HeadingFont(10.5f); currentName.ForeColor = AppTheme.TextPrimary; currentName.Location = new Point(16, 33); currentName.Size = new Size(580, 22); currentName.AutoEllipsis = true;
        currentPath.ForeColor = AppTheme.TextSecondary; currentPath.Location = new Point(16, 56); currentPath.Size = new Size(580, 18); currentPath.AutoEllipsis = true; currentPath.Font = AppTheme.BodyFont(8.5f);
        currentCard.Controls.AddRange(new Control[] { currentCaption, currentName, currentPath });
        Controls.Add(currentCard);

        stageText.Location = new Point(30, 360); stageText.Size = new Size(620, 54); stageText.TextAlign = ContentAlignment.MiddleCenter; stageText.ForeColor = AppTheme.Primary; stageText.Font = AppTheme.ButtonFont();
        stageText.Text = "1. Сканирование    ●────    2. Копирование    ○────    3. Раскладка    ○────    4. Проверка";
        Controls.Add(stageText);

        backgroundButton.Location = new Point(198, 430); backgroundButton.Size = new Size(250, 46); backgroundButton.BackColor = AppTheme.Surface; backgroundButton.ForeColor = AppTheme.Primary;
        backgroundButton.Click += (_, _) => Hide();
        cancelButton.Location = new Point(462, 430); cancelButton.Size = new Size(188, 46); cancelButton.BackColor = Color.FromArgb(255, 247, 248); cancelButton.ForeColor = AppTheme.Danger;
        cancelButton.Click += (_, _) => CancelTransfer();
        Controls.AddRange(new Control[] { backgroundButton, cancelButton });

        FormClosing += (_, e) =>
        {
            if (terminal) return;
            e.Cancel = true;
            Hide();
        };
    }

    RoundedPanel CreateStatCard(Point location, string icon, string caption, Label value)
    {
        var card = new RoundedPanel { Location = location, Size = new Size(145, 68), Radius = 12, BackColor = AppTheme.Surface };
        var iconLabel = new Label { Text = icon, AutoSize = true, Font = AppTheme.HeadingFont(14), ForeColor = AppTheme.Primary, Location = new Point(12, 19) };
        var captionLabel = new Label { Text = caption, AutoSize = true, Font = AppTheme.BodyFont(8.5f), ForeColor = AppTheme.TextSecondary, Location = new Point(43, 10) };
        value.Text = caption == "Файлов" ? "0" : caption == "Ошибки" ? "0" : "—";
        value.Font = AppTheme.HeadingFont(10.5f); value.ForeColor = AppTheme.TextPrimary; value.Location = new Point(43, 31); value.Size = new Size(94, 24);
        card.Controls.AddRange(new Control[] { iconLabel, captionLabel, value });
        return card;
    }

    public void Apply(TransferSnapshot snapshot)
    {
        if (IsDisposed) return;
        var value = (int)Math.Round(snapshot.Fraction * 1000);
        progress.Value = Math.Clamp(value, 0, 1000);
        percent.Text = $"{snapshot.Fraction:P0}";
        count.Text = $"{snapshot.CompletedFiles:N0} из {snapshot.TotalFiles:N0} файлов";
        fileValue.Text = $"{snapshot.CompletedFiles:N0}/{snapshot.TotalFiles:N0}";
        speedValue.Text = FormatSpeed(snapshot.BytesPerSecond);
        etaValue.Text = snapshot.Remaining is { } remaining ? FormatDuration(remaining) : "—";
        errorsValue.Text = snapshot.Errors.ToString("N0");
        errorsValue.ForeColor = snapshot.Errors == 0 ? AppTheme.TextPrimary : AppTheme.Danger;
        currentName.Text = snapshot.CurrentFile;
        currentPath.Text = snapshot.CurrentFolder;
        stageText.Text = StageLine(snapshot.Stage);

        if (snapshot.Stage == TransferStage.Preparing)
        {
            title.Text = "Подготовка файлов";
            subtitle.Text = "Определяем размеры и создаём очередь копирования";
        }
        else if (snapshot.Stage == TransferStage.Completed)
        {
            terminal = true;
            title.Text = snapshot.Errors == 0 ? "Сортировка завершена" : "Сортировка завершена с ошибками";
            subtitle.Text = snapshot.Errors == 0 ? "Все выбранные файлы скопированы и разложены по датам" : $"Не удалось скопировать файлов: {snapshot.Errors}";
            progress.Value = 1000; percent.Text = "100%";
            backgroundButton.Text = "Открыть папку";
            backgroundButton.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(snapshot.CurrentFolder) { UseShellExecute = true }); } catch { } };
            cancelButton.Text = "Закрыть";
            cancelButton.Click -= (_, _) => CancelTransfer();
        }
    }

    public void ShowCancelled()
    {
        terminal = true; title.Text = "Сортировка отменена"; subtitle.Text = "Уже скопированные файлы сохранены"; cancelButton.Text = "Закрыть";
    }

    public void ShowFailure(string message)
    {
        terminal = true; title.Text = "Не удалось завершить сортировку"; subtitle.Text = message; cancelButton.Text = "Закрыть";
    }

    void CancelTransfer()
    {
        if (terminal) { Close(); return; }
        if (MessageBox.Show(this, "Остановить сортировку? Уже скопированные файлы останутся в папке назначения.", "Отмена сортировки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            cancellation.Cancel();
    }

    static string StageLine(TransferStage stage) => stage switch
    {
        TransferStage.Preparing => "●  1. Сканирование    ────    ○  2. Копирование    ────    ○  3. Раскладка    ────    ○  4. Проверка",
        TransferStage.Copying => "✓  1. Сканирование    ────    ●  2. Копирование    ────    ○  3. Раскладка    ────    ○  4. Проверка",
        TransferStage.Organizing => "✓  1. Сканирование    ────    ✓  2. Копирование    ────    ●  3. Раскладка    ────    ○  4. Проверка",
        TransferStage.Verifying => "✓  1. Сканирование    ────    ✓  2. Копирование    ────    ✓  3. Раскладка    ────    ●  4. Проверка",
        TransferStage.Completed => "✓  Сканирование    ────    ✓  Копирование    ────    ✓  Раскладка    ────    ✓  Проверка",
        _ => "Сортировка остановлена"
    };

    static string FormatSpeed(double value) => value <= 0 ? "—" : value >= 1024 * 1024 ? $"{value / 1024 / 1024:0.0} МБ/с" : $"{value / 1024:0} КБ/с";
    static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? $"~ {value:h\\:mm\\:ss}" : $"~ {value:mm\\:ss}";
}
