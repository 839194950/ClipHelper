using LocalClipboard.App.IntegrationTests.Windows;
using LocalClipboard.App.UI;
using LocalClipboard.Infrastructure.Settings;
using System.Windows.Forms;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class SettingsFormTests
{
    [Fact]
    public Task SettingsForm_ShowsDefaultHotkeyAndStartupState() => StaTest.RunAsync(() =>
    {
        using SettingsForm form = SettingsForm.CreateForTest(
            AppSettings.Default,
            cacheBytes: 25 * 1024 * 1024);

        Assert.True(form.StartWithWindowsChecked);
        Assert.Equal("Alt + V", form.HotkeyDisplayText);
        Assert.Contains("25", form.CacheUsageText);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClearDialog_DefaultsToProtectFavorites() => StaTest.RunAsync(() =>
    {
        using var dialog = new ClearHistoryDialog();

        Assert.False(dialog.IncludeFavorites);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SettingsForm_AppliesEnglishLanguage() => StaTest.RunAsync(() =>
    {
        AppSettings settings = AppSettings.Default with { Language = AppLanguage.English };
        using SettingsForm form = SettingsForm.CreateForTest(settings, cacheBytes: 0);

        Assert.Equal("Settings", form.Text);
        Assert.Contains(form.Controls.Cast<Control>(), control => control.Text == "Start with Windows");
        Assert.Contains(form.Controls.Cast<Control>(), control => control.Text == "Save");
        return Task.CompletedTask;
    });
}
