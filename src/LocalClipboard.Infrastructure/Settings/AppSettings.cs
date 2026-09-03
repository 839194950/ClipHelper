using System.Windows.Forms;

namespace LocalClipboard.Infrastructure.Settings;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

public enum AppLanguage
{
    Chinese,
    English,
}

public sealed record AppSettings(
    bool StartWithWindows,
    HotkeyModifiers HotkeyModifiers,
    Keys HotkeyKey,
    AppLanguage Language = AppLanguage.English)
{
    public static AppSettings Default { get; } = new(true, HotkeyModifiers.Alt, Keys.V, AppLanguage.English);
}
