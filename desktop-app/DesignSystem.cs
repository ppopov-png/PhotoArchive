using System.Drawing;

namespace PhotoArchive;

internal static class AppTheme
{
    // Core surfaces
    public static readonly Color Background = Color.FromArgb(247, 248, 252);
    public static readonly Color Surface = Color.FromArgb(255, 255, 255);
    public static readonly Color SurfaceSoft = Color.FromArgb(249, 250, 255);
    public static readonly Color SurfaceMuted = Color.FromArgb(244, 246, 252);

    // Brand / action colors
    public static readonly Color Primary = Color.FromArgb(88, 101, 232);
    public static readonly Color PrimaryHover = Color.FromArgb(75, 88, 219);
    public static readonly Color PrimaryPressed = Color.FromArgb(65, 76, 201);
    public static readonly Color PrimarySoft = Color.FromArgb(241, 242, 255);
    public static readonly Color PrimarySoftHover = Color.FromArgb(233, 235, 255);

    // Text
    public static readonly Color TextPrimary = Color.FromArgb(31, 34, 48);
    public static readonly Color TextSecondary = Color.FromArgb(112, 118, 140);
    public static readonly Color TextMuted = Color.FromArgb(150, 155, 174);

    // Lines / states
    public static readonly Color Border = Color.FromArgb(229, 232, 241);
    public static readonly Color BorderStrong = Color.FromArgb(215, 219, 232);
    public static readonly Color Success = Color.FromArgb(46, 189, 112);
    public static readonly Color Danger = Color.FromArgb(231, 75, 91);
    public static readonly Color DangerSoft = Color.FromArgb(255, 246, 248);
    public static readonly Color Warning = Color.FromArgb(231, 164, 57);

    // Shared geometry
    public const int RadiusSmall = 10;
    public const int RadiusMedium = 14;
    public const int RadiusLarge = 20;
    public const int RadiusXLarge = 24;

    // 4px spacing scale
    public const int Space1 = 4;
    public const int Space2 = 8;
    public const int Space3 = 12;
    public const int Space4 = 16;
    public const int Space5 = 24;
    public const int Space6 = 32;
    public const int Space7 = 40;

    public static Font TitleFont(float size = 24f) => new("Segoe UI Variable Display", size, FontStyle.Bold);
    public static Font HeadingFont(float size = 14f) => new("Segoe UI Variable Text Semibold", size, FontStyle.Bold);
    public static Font BodyFont(float size = 10f) => new("Segoe UI Variable Text", size, FontStyle.Regular);
    public static Font ButtonFont(float size = 9.5f) => new("Segoe UI Variable Text Semibold", size, FontStyle.Regular);
    public static Font CaptionFont(float size = 8.8f) => new("Segoe UI Variable Text", size, FontStyle.Regular);

    public static Color WithAlpha(Color color, int alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
}
