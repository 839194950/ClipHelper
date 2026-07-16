using System.Text.Json;

namespace LocalClipboard.Infrastructure.Settings;

public sealed class JsonSettingsStore
{
    private readonly string settingsPath;
    private readonly string recoveryDirectory;
    private readonly Action<string> deleteFile;

    public JsonSettingsStore(string settingsPath, string recoveryDirectory)
        : this(settingsPath, recoveryDirectory, File.Delete)
    {
    }

    internal JsonSettingsStore(
        string settingsPath,
        string recoveryDirectory,
        Action<string> deleteFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryDirectory);
        ArgumentNullException.ThrowIfNull(deleteFile);
        this.settingsPath = settingsPath;
        this.recoveryDirectory = recoveryDirectory;
        this.deleteFile = deleteFile;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(settingsPath)) return AppSettings.Default;

        try
        {
            string json = await File.ReadAllTextAsync(settingsPath, cancellationToken);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default;
        }
        catch (JsonException)
        {
            return MoveInvalidSettingsToRecovery();
        }
        catch (NotSupportedException)
        {
            return MoveInvalidSettingsToRecovery();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
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
                await JsonSerializer.SerializeAsync(stream, settings, options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
            ownsTemporaryFile = false;
        }
        catch
        {
            if (ownsTemporaryFile && File.Exists(temporaryPath)) deleteFile(temporaryPath);

            throw;
        }
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
