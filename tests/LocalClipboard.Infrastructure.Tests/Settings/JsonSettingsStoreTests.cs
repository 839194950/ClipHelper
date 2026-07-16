using System.Globalization;
using System.Windows.Forms;
using LocalClipboard.Infrastructure.Settings;

namespace LocalClipboard.Infrastructure.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        "LocalClipboardSettingsTests",
        Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(rootPath, "config", "settings.json");

    private string RecoveryDirectory => Path.Combine(rootPath, "recovery");

    public void Dispose()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaultsWhenFileDoesNotExist()
    {
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);

        AppSettings settings = await store.LoadAsync(default);

        Assert.Equal(AppSettings.Default, settings);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);
        var expected = new AppSettings(
            false,
            HotkeyModifiers.Control | HotkeyModifiers.Shift,
            Keys.Space);

        await store.SaveAsync(expected, default);

        Assert.Equal(expected, await store.LoadAsync(default));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public async Task LoadAsync_MovesInvalidJsonToRecoveryAndReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, "{ invalid json");
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);
        DateTime beforeLoad = DateTime.UtcNow;

        AppSettings settings = await store.LoadAsync(default);

        DateTime afterLoad = DateTime.UtcNow;
        Assert.Equal(AppSettings.Default, settings);
        Assert.False(File.Exists(SettingsPath));
        string recoveryFile = Assert.Single(Directory.GetFiles(RecoveryDirectory));
        string recoveryFileName = Path.GetFileName(recoveryFile);
        Assert.Matches(
            @"^settings-\d{8}-\d{13}\.invalid\.json$",
            recoveryFileName);
        string timestampText = recoveryFileName["settings-".Length..^".invalid.json".Length];
        DateTime timestamp = DateTime.ParseExact(
            timestampText,
            "yyyyMMdd-HHmmssfffffff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        Assert.InRange(timestamp, beforeLoad, afterLoad);
    }

    [Fact]
    public async Task SaveAsync_CreatesParentDirectory()
    {
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);

        await store.SaveAsync(AppSettings.Default, default);

        Assert.True(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task SaveAsync_PreCanceledTokenPreservesExistingSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        const string existingContent = "existing settings";
        const string existingTemporaryContent = "another save's temporary settings";
        await File.WriteAllTextAsync(SettingsPath, existingContent);
        await File.WriteAllTextAsync(SettingsPath + ".tmp", existingTemporaryContent);
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(AppSettings.Default, cancellation.Token));

        Assert.Equal(existingContent, await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(existingTemporaryContent, await File.ReadAllTextAsync(SettingsPath + ".tmp"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAsync_WhenCleanupFails_PropagatesCleanupIoException(bool unauthorized)
    {
        Directory.CreateDirectory(SettingsPath);
        Exception expected = unauthorized
            ? new UnauthorizedAccessException("cleanup denied")
            : new IOException("cleanup failed");
        var store = new JsonSettingsStore(
            SettingsPath,
            RecoveryDirectory,
            _ => throw expected);

        Exception actual = await Assert.ThrowsAnyAsync<Exception>(() =>
            store.SaveAsync(AppSettings.Default, default));

        Assert.Same(expected, actual);
    }
}
