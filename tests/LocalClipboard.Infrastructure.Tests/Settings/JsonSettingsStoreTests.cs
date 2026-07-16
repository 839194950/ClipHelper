using System.Globalization;
using System.Text.Json;
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

    public static TheoryData<string> InvalidHotkeyKeyJsonValues => new()
    {
        ((int)Keys.Control).ToString(CultureInfo.InvariantCulture),
        ((int)Keys.KeyCode).ToString(CultureInfo.InvariantCulture),
        ((int)Keys.Modifiers).ToString(CultureInfo.InvariantCulture),
        ((int)Keys.None).ToString(CultureInfo.InvariantCulture),
        "999999",
        JsonSerializer.Serialize("Space"),
        "true",
        "{}",
    };

    public static TheoryData<string> InvalidHotkeyModifierJsonValues => new()
    {
        "-1",
        "16",
        "4294967296",
        JsonSerializer.Serialize("Control"),
        "true",
        "{}",
    };

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
    public async Task LoadAsync_MergesMissingFieldsWithDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, """{"StartWithWindows":true}""");
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);

        AppSettings settings = await store.LoadAsync(default);

        Assert.Equal(AppSettings.Default, settings);
        Assert.Empty(Directory.Exists(RecoveryDirectory)
            ? Directory.GetFiles(RecoveryDirectory)
            : []);
    }

    [Theory]
    [MemberData(nameof(InvalidHotkeyKeyJsonValues))]
    public async Task LoadAsync_InvalidHotkeyKeyFallsBackWithoutDiscardingValidFields(
        string hotkeyKeyJson)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string json = $$"""{"StartWithWindows":false,"HotkeyModifiers":2,"HotkeyKey":{{hotkeyKeyJson}}}""";
        await File.WriteAllTextAsync(SettingsPath, json);
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);

        AppSettings settings = await store.LoadAsync(default);

        Assert.False(settings.StartWithWindows);
        Assert.Equal(HotkeyModifiers.Control, settings.HotkeyModifiers);
        Assert.Equal(Keys.V, settings.HotkeyKey);
        Assert.True(File.Exists(SettingsPath));
        Assert.False(Directory.Exists(RecoveryDirectory));
    }

    [Theory]
    [MemberData(nameof(InvalidHotkeyModifierJsonValues))]
    public async Task LoadAsync_InvalidHotkeyModifiersFallBackWithoutDiscardingValidFields(
        string hotkeyModifiersJson)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string json = $$"""{"StartWithWindows":false,"HotkeyModifiers":{{hotkeyModifiersJson}},"HotkeyKey":32}""";
        await File.WriteAllTextAsync(SettingsPath, json);
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);

        AppSettings settings = await store.LoadAsync(default);

        Assert.False(settings.StartWithWindows);
        Assert.Equal(HotkeyModifiers.Alt, settings.HotkeyModifiers);
        Assert.Equal(Keys.Space, settings.HotkeyKey);
        Assert.True(File.Exists(SettingsPath));
        Assert.False(Directory.Exists(RecoveryDirectory));
    }

    [Theory]
    [InlineData((int)Keys.A)]
    [InlineData((int)Keys.D7)]
    [InlineData((int)Keys.F12)]
    [InlineData((int)Keys.Space)]
    public async Task LoadAsync_UsableHotkeyKeyCodesArePreserved(int hotkeyKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string json = $$"""{"StartWithWindows":false,"HotkeyModifiers":2,"HotkeyKey":{{hotkeyKey}}}""";
        await File.WriteAllTextAsync(SettingsPath, json);
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);

        AppSettings settings = await store.LoadAsync(default);

        Assert.False(settings.StartWithWindows);
        Assert.Equal(HotkeyModifiers.Control, settings.HotkeyModifiers);
        Assert.Equal((Keys)hotkeyKey, settings.HotkeyKey);
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

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public async Task LoadAsync_NonObjectRootMovesFileToRecovery(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, json);
        var store = new JsonSettingsStore(SettingsPath, RecoveryDirectory);

        AppSettings settings = await store.LoadAsync(default);

        Assert.Equal(AppSettings.Default, settings);
        Assert.False(File.Exists(SettingsPath));
        Assert.Single(Directory.GetFiles(RecoveryDirectory));
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

    [Fact]
    public async Task LoadAsync_InvalidRecoveryCannotMoveAConcurrentSuccessfulSave()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        const string invalidJson = "{ invalid json";
        await File.WriteAllTextAsync(SettingsPath, invalidJson);
        var invalidRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveReachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pendingSaveLock = null;
        var loadingStore = new JsonSettingsStore(
            SettingsPath,
            RecoveryDirectory,
            File.Delete,
            beforeRecovery: _ =>
            {
                invalidRead.TrySetResult();
                return allowRecovery.Task;
            });
        var savingStore = new JsonSettingsStore(
            Path.GetFullPath(SettingsPath),
            RecoveryDirectory,
            File.Delete,
            lockWaitStarted: waitTask =>
            {
                pendingSaveLock = waitTask;
                saveReachedLock.TrySetResult();
            });
        var expected = new AppSettings(false, HotkeyModifiers.Control, Keys.Space);

        Task<AppSettings> loadTask = loadingStore.LoadAsync(default);
        await invalidRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task saveTask = savingStore.SaveAsync(expected, default);

        try
        {
            await saveReachedLock.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(pendingSaveLock);
            Assert.False(pendingSaveLock.IsCompleted);
        }
        finally
        {
            allowRecovery.TrySetResult();
        }

        Assert.Equal(AppSettings.Default, await loadTask);
        await saveTask;
        Assert.Equal(expected, await new JsonSettingsStore(SettingsPath, RecoveryDirectory).LoadAsync(default));
        string recoveredFile = Assert.Single(Directory.GetFiles(RecoveryDirectory));
        Assert.Equal(invalidJson, await File.ReadAllTextAsync(recoveredFile));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentStoresShareFixedTemporaryFileSafely()
    {
        string alternatePath = Path.Combine(
            Path.GetDirectoryName(SettingsPath)!,
            ".",
            Path.GetFileName(SettingsPath));
        AppSettings[] candidates = Enumerable.Range(0, 100)
            .Select(index => new AppSettings(
                index % 2 == 0,
                index % 3 == 0 ? HotkeyModifiers.Control : HotkeyModifiers.Shift,
                index % 5 == 0 ? Keys.Space : Keys.V))
            .ToArray();

        Task[] saves = candidates
            .Select((settings, index) => new JsonSettingsStore(
                    index % 2 == 0 ? SettingsPath : alternatePath,
                    RecoveryDirectory)
                .SaveAsync(settings, default))
            .ToArray();

        await Task.WhenAll(saves);

        string json = await File.ReadAllTextAsync(SettingsPath);
        AppSettings? saved = JsonSerializer.Deserialize<AppSettings>(json);
        Assert.NotNull(saved);
        Assert.Contains(saved, candidates);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAsync_WhenCleanupFails_AggregatesSaveAndCleanupIoExceptions(bool unauthorized)
    {
        Directory.CreateDirectory(SettingsPath);
        Exception cleanupFailure = unauthorized
            ? new UnauthorizedAccessException("cleanup denied")
            : new IOException("cleanup failed");
        var store = new JsonSettingsStore(
            SettingsPath,
            RecoveryDirectory,
            _ => throw cleanupFailure);

        AggregateException aggregate = await Assert.ThrowsAsync<AggregateException>(() =>
            store.SaveAsync(AppSettings.Default, default));

        Assert.Contains(cleanupFailure, aggregate.InnerExceptions);
        Assert.Contains(aggregate.InnerExceptions, exception =>
            exception is IOException or UnauthorizedAccessException &&
            !ReferenceEquals(exception, cleanupFailure));
    }

    [Fact]
    public async Task SaveAsync_WhenCanceledAndCleanupFails_AggregatesBothExceptions()
    {
        var cancellationFailure = new OperationCanceledException("save canceled");
        var cleanupFailure = new IOException("cleanup failed");
        var store = new JsonSettingsStore(
            SettingsPath,
            RecoveryDirectory,
            _ => throw cleanupFailure,
            afterTemporaryFileCreated: _ => throw cancellationFailure);

        AggregateException aggregate = await Assert.ThrowsAsync<AggregateException>(() =>
            store.SaveAsync(AppSettings.Default, default));

        Assert.Contains(cancellationFailure, aggregate.InnerExceptions);
        Assert.Contains(cleanupFailure, aggregate.InnerExceptions);
    }
}
