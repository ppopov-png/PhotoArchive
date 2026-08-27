using System.Drawing;

namespace PhotoArchive;

internal static class AppTheme
{
    public static readonly Color Background = Color.FromArgb(247, 248, 252);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceSoft = Color.FromArgb(247, 248, 255);
    public static readonly Color Primary = Color.FromArgb(88, 101, 232);
    public static readonly Color PrimaryHover = Color.FromArgb(72, 85, 214);
    public static readonly Color PrimarySoft = Color.FromArgb(241, 242, 255);
    public static readonly Color TextPrimary = Color.FromArgb(32, 34, 48);
    public static readonly Color TextSecondary = Color.FromArgb(116, 120, 141);
    public static readonly Color Border = Color.FromArgb(230, 232, 240);
    public static readonly Color Success = Color.FromArgb(46, 189, 112);
    public static readonly Color Danger = Color.FromArgb(231, 75, 91);
    public static readonly Color Warning = Color.FromArgb(231, 164, 57);

    public const int RadiusSmall = 10;
    public const int RadiusMedium = 14;
    public const int RadiusLarge = 20;
    public const int Space1 = 4;
    public const int Space2 = 8;
    public const int Space3 = 12;
    public const int Space4 = 16;
    public const int Space5 = 24;
    public const int Space6 = 32;

    public static Font TitleFont(float size = 24f) => new("Segoe UI Semibold", size, FontStyle.Bold);
    public static Font HeadingFont(float size = 14f) => new("Segoe UI Semibold", size, FontStyle.Bold);
    public static Font BodyFont(float size = 10f) => new("Segoe UI", size, FontStyle.Regular);
    public static Font ButtonFont(float size = 9.5f) => new("Segoe UI Semibold", size, FontStyle.Regular);
}
