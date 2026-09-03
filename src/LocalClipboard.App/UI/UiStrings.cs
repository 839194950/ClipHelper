using LocalClipboard.Infrastructure.Settings;

namespace LocalClipboard.App.UI;

internal static class UiStrings
{
    internal static string SettingsTitle(AppLanguage language) => language == AppLanguage.English ? "Settings" : "设置";
    internal static string StartWithWindows(AppLanguage language) => language == AppLanguage.English ? "Start with Windows" : "随 Windows 启动";
    internal static string GlobalHotkey(AppLanguage language) => language == AppLanguage.English ? "Global hotkey" : "全局快捷键";
    internal static string DataDirectory(AppLanguage language) => language == AppLanguage.English ? "Data directory" : "数据目录";
    internal static string OpenDataDirectory(AppLanguage language) => language == AppLanguage.English ? "Open data directory" : "打开数据目录";
    internal static string Retention(AppLanguage language) => language == AppLanguage.English ? "Regular history: up to 500 items / 30 days" : "普通历史：最多 500 条 / 30 天";
    internal static string CacheUsage(AppLanguage language, string value) => language == AppLanguage.English ? $"Image cache: {value}" : $"当前图片缓存：{value}";
    internal static string Save(AppLanguage language) => language == AppLanguage.English ? "Save" : "保存";
    internal static string Cancel(AppLanguage language) => language == AppLanguage.English ? "Cancel" : "取消";
    internal static string Language(AppLanguage language) => language == AppLanguage.English ? "Language" : "语言";
}
