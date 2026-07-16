using System.Collections.Concurrent;
using System.Text.Json;

namespace LocalClipboard.Infrastructure.Settings;

public sealed class JsonSettingsStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SettingsLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string settingsPath;
    private readonly string recoveryDirectory;
    private readonly Action<string> deleteFile;
    private readonly Action<Task>? lockWaitStarted;
    private readonly Func<CancellationToken, Task>? beforeRecovery;
    private readonly Func<CancellationToken, Task>? afterTemporaryFileCreated;
    private readonly SemaphoreSlim settingsLock;

    public JsonSettingsStore(string settingsPath, string recoveryDirectory)
        : this(settingsPath, recoveryDirectory, File.Delete)
    {
    }

    internal JsonSettingsStore(
        string settingsPath,
        string recoveryDirectory,
        Action<string> deleteFile,
        Action<Task>? lockWaitStarted = null,
        Func<CancellationToken, Task>? beforeRecovery = null,
        Func<CancellationToken, Task>? afterTemporaryFileCreated = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        ArgumentNullException.ThrowIfNull(deleteFile);
        this.settingsPath = Path.GetFullPath(settingsPath);
        this.recoveryDirectory = Path.GetFullPath(recoveryDirectory);
        this.deleteFile = deleteFile;
        this.lockWaitStarted = lockWaitStarted;
        this.beforeRecovery = beforeRecovery;
        this.afterTemporaryFileCreated = afterTemporaryFileCreated;
        settingsLock = SettingsLocks.GetOrAdd(this.settingsPath, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        Task lockWait = settingsLock.WaitAsync(cancellationToken);
        lockWaitStarted?.Invoke(lockWait);
        await lockWait;

        try
        {
            if (!File.Exists(settingsPath)) return AppSettings.Default;

            string json = await File.ReadAllTextAsync(settingsPath, cancellationToken);
            AppSettingsDto? settings = JsonSerializer.Deserialize<AppSettingsDto>(json);
            return MergeWithDefaults(settings);
        }
        catch (JsonException)
        {
            if (beforeRecovery is not null) await beforeRecovery(cancellationToken);
            return MoveInvalidSettingsToRecovery();
        }
        catch (NotSupportedException)
        {
            if (beforeRecovery is not null) await beforeRecovery(cancellationToken);
            return MoveInvalidSettingsToRecovery();
        }
        finally
        {
            settingsLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Task lockWait = settingsLock.WaitAsync(cancellationToken);
        lockWaitStarted?.Invoke(lockWait);
        await lockWait;

        try
        {
            await SaveCoreAsync(settings, cancellationToken);
        }
        finally
        {
            settingsLock.Release();
        }
    }

    private async Task SaveCoreAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        string? parentDirectory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(parentDirectory)) Directory.CreateDirectory(parentDirectory);

        string temporaryPath = settingsPath + ".tmp";
        bool ownsTemporaryFile = false;
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                ownsTemporaryFile = true;
                if (afterTemporaryFileCreated is not null)
                    await afterTemporaryFileCreated(cancellationToken);
                await JsonSerializer.SerializeAsync(stream, settings, options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
            ownsTemporaryFile = false;
        }
        catch (Exception saveException)
        {
            if (ownsTemporaryFile && File.Exists(temporaryPath))
            {
                try
                {
                    deleteFile(temporaryPath);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(saveException, cleanupException);
                }
            }

            throw;
        }
    }

    private static AppSettings MergeWithDefaults(AppSettingsDto? settings)
    {
        if (settings is null) return AppSettings.Default;

        const HotkeyModifiers allowedModifiers =
            HotkeyModifiers.Alt |
            HotkeyModifiers.Control |
            HotkeyModifiers.Shift |
            HotkeyModifiers.Windows;
        HotkeyModifiers hotkeyModifiers = settings.HotkeyModifiers is uint rawModifiers &&
            (rawModifiers & ~(uint)allowedModifiers) == 0
                ? (HotkeyModifiers)rawModifiers
                : AppSettings.Default.HotkeyModifiers;
        Keys hotkeyKey = settings.HotkeyKey is int rawKey &&
            rawKey != (int)Keys.None &&
            Enum.IsDefined(typeof(Keys), rawKey)
                ? (Keys)rawKey
                : AppSettings.Default.HotkeyKey;

        return new AppSettings(
            settings.StartWithWindows ?? AppSettings.Default.StartWithWindows,
            hotkeyModifiers,
            hotkeyKey);
    }

    private AppSettings MoveInvalidSettingsToRecovery()
    {
        Directory.CreateDirectory(recoveryDirectory);
        string recoveryPath = Path.Combine(
            recoveryDirectory,
            $"settings-{DateTime.UtcNow:yyyyMMdd-HHmmssfffffff}.invalid.json");

        File.Move(settingsPath, recoveryPath);
        return AppSettings.Default;
    }

    private sealed record AppSettingsDto(
        bool? StartWithWindows,
        uint? HotkeyModifiers,
        int? HotkeyKey);
}
