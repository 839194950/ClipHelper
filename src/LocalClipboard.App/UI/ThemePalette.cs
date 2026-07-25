using Microsoft.Win32;

namespace LocalClipboard.App.UI;

internal sealed record ThemePalette(
    Color Background,
    Color Surface,
    Color Border,
    Color PrimaryText,
    Color SecondaryText,
    Color Accent,
    Color Selection)
{
    public Color MutedSurface => Blend(Surface, Background, 0.45f);
    public Color SoftAccent => Blend(Accent, Surface, 0.82f);
    public Color HoverBorder => Blend(Accent, Border, 0.38f);
    public Color Shadow => Color.FromArgb(28, Color.Black);

    private static Color Blend(Color first, Color second, float secondWeight)
    {
        float firstWeight = 1f - secondWeight;
        return Color.FromArgb(
            (int)(first.A * firstWeight + second.A * secondWeight),
            (int)(first.R * firstWeight + second.R * secondWeight),
            (int)(first.G * firstWeight + second.G * secondWeight),
            (int)(first.B * firstWeight + second.B * secondWeight));
    }

    public static ThemePalette ReadCurrent()
    {
        if (SystemInformation.HighContrast)
        {
            return new(
                SystemColors.Window,
                SystemColors.Control,
                SystemColors.WindowFrame,
                SystemColors.WindowText,
                SystemColors.GrayText,
                SystemColors.Highlight,
                SystemColors.Highlight);
        }

        bool useLightTheme = true;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value) useLightTheme = value != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            useLightTheme = true;
        }

        return useLightTheme
            ? new(
                Color.FromArgb(246, 247, 249),
                Color.White,
                Color.FromArgb(218, 221, 226),
                Color.FromArgb(31, 35, 40),
                Color.FromArgb(99, 107, 116),
                Color.FromArgb(62, 116, 210),
                Color.FromArgb(226, 235, 250))
            : new(
                Color.FromArgb(28, 30, 34),
                Color.FromArgb(38, 41, 46),
                Color.FromArgb(70, 74, 81),
                Color.FromArgb(238, 240, 243),
                Color.FromArgb(166, 173, 181),
                Color.FromArgb(104, 156, 255),
                Color.FromArgb(55, 73, 101));
    }
}
