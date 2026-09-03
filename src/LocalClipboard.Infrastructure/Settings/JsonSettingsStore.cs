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
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Settings JSON root must be an object.");
            return ParseSettings(document.RootElement);
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

    private static AppSettings ParseSettings(JsonElement root)
    {
        bool startWithWindows = AppSettings.Default.StartWithWindows;
        if (root.TryGetProperty(nameof(AppSettings.StartWithWindows), out JsonElement startElement) &&
            startElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            startWithWindows = startElement.GetBoolean();

        HotkeyModifiers hotkeyModifiers = ParseHotkeyModifiers(root);
        Keys hotkeyKey = ParseHotkeyKey(root);
        AppLanguage language = ParseLanguage(root);

        return new AppSettings(
            startWithWindows,
            hotkeyModifiers,
            hotkeyKey,
            language);
    }

    private static AppLanguage ParseLanguage(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(AppSettings.Language), out JsonElement element))
            return AppSettings.Default.Language;

        AppLanguage language;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int rawLanguage))
            language = (AppLanguage)rawLanguage;
        else if (element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), ignoreCase: true, out AppLanguage parsedLanguage))
            language = parsedLanguage;
        else
            return AppSettings.Default.Language;

        if (!Enum.IsDefined(language)) return AppSettings.Default.Language;

        return language;
    }

    private static HotkeyModifiers ParseHotkeyModifiers(JsonElement root)
    {
        const int allowedBits =
            (int)(HotkeyModifiers.Alt |
                HotkeyModifiers.Control |
                HotkeyModifiers.Shift |
                HotkeyModifiers.Windows);
        if (!root.TryGetProperty(nameof(AppSettings.HotkeyModifiers), out JsonElement element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out int rawModifiers) ||
            rawModifiers < 0 ||
            (rawModifiers & ~allowedBits) != 0)
            return AppSettings.Default.HotkeyModifiers;

        return (HotkeyModifiers)rawModifiers;
    }

    private static Keys ParseHotkeyKey(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(AppSettings.HotkeyKey), out JsonElement element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out int rawKey) ||
            rawKey < 0)
            return AppSettings.Default.HotkeyKey;

        var rawKeys = (Keys)rawKey;
        Keys keyCode = rawKeys & Keys.KeyCode;
        return IsUsableHotkeyKey(rawKeys, keyCode)
            ? keyCode
            : AppSettings.Default.HotkeyKey;
    }

    private static bool IsUsableHotkeyKey(Keys rawKeys, Keys keyCode)
    {
        if ((rawKeys & Keys.Modifiers) != Keys.None || keyCode == Keys.None)
            return false;
        if (!Enum.IsDefined(typeof(Keys), keyCode)) return false;

        return rawKeys is not Keys.KeyCode and
            not Keys.Modifiers and
            not Keys.Shift and
            not Keys.Control and
            not Keys.Alt;
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

}
