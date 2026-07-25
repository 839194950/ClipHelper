using System.Globalization;
using System.Security.Principal;
using LocalClipboard.Core.Models;
using LocalClipboard.Core.Services;
using LocalClipboard.Infrastructure.Diagnostics;
using LocalClipboard.Infrastructure.Settings;
using LocalClipboard.Infrastructure.Storage;
using LocalClipboard.Infrastructure.Windows;
using Microsoft.Data.Sqlite;

namespace LocalClipboard.App;

internal sealed record RepositoryOpenResult(
    SqliteHistoryRepository Repository,
    bool RecoveredCorruption);

internal static class Program
{
    private const long MaximumLogBytes = 5_242_880;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        AppPaths? paths = null;
        RollingFileLogger? logger = null;
        try
        {
            paths = AppPaths.CreateDefault();
            logger = new RollingFileLogger(paths.Logs, MaximumLogBytes);
            Run(paths, logger);
        }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            TryLogFatal(logger, exception);
            string dataDirectory = paths?.Root ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LocalClipboard");
            MessageBox.Show(
                $"ClipHelper 启动失败（{GetErrorCategory(exception)}）。\n\n数据目录：{dataDirectory}",
                "ClipHelper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    internal static async Task<RepositoryOpenResult> OpenRepositoryAsync(
        AppPaths paths,
        CancellationToken cancellationToken)
    {
        var repository = new SqliteHistoryRepository(paths.Database);
        try
        {
            await repository.GetLatestAsync(cancellationToken);
            return new RepositoryOpenResult(repository, false);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 11 or 26)
        {
            SqliteConnection.ClearAllPools();
            MoveCorruptDatabaseFiles(paths, DateTimeOffset.UtcNow);

            repository = new SqliteHistoryRepository(paths.Database);
            await repository.GetLatestAsync(cancellationToken);
            return new RepositoryOpenResult(repository, true);
        }
    }

    internal static void MoveCorruptDatabaseFiles(AppPaths paths, DateTimeOffset timestamp)
    {
        Directory.CreateDirectory(paths.Recovery);
        string suffix = timestamp.ToUniversalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        foreach (string source in new[] { paths.Database, paths.Database + "-wal", paths.Database + "-shm" })
        {
            if (!File.Exists(source)) continue;
            string destination = Path.Combine(paths.Recovery, $"{Path.GetFileName(source)}-{suffix}.corrupt");
            File.Move(source, destination);
        }
    }

    private static void Run(AppPaths paths, RollingFileLogger logger)
    {
        string userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
        var singleInstance = new SingleInstanceCoordinator($"LocalClipboard.{userSid}");
        try
        {
            if (!singleInstance.TryAcquirePrimary())
            {
                singleInstance.SendShowMessageAsync(CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            var settingsStore = new JsonSettingsStore(paths.Settings, paths.Recovery);
            AppSettings settings = settingsStore.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var startupManager = new StartupManager();
            if (settings.StartWithWindows)
            {
                startupManager.SetEnabled(
                    Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable."),
                    true);
            }

            RepositoryOpenResult openResult = OpenRepositoryAsync(paths, CancellationToken.None).GetAwaiter().GetResult();
            var imageStore = new PngImageStore(paths.ImagesRoot);
            IReadOnlyList<ClipboardEntry> entries = openResult.Repository
                .GetAllAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var referencedPaths = entries
                .SelectMany(entry => new[] { entry.ImagePath, entry.ThumbnailPath })
                .Where(path => path is not null)
                .Select(path => path!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            imageStore.DeleteOrphansAsync(referencedPaths, CancellationToken.None).GetAwaiter().GetResult();

            var historyService = new HistoryService(openResult.Repository, imageStore, RetentionLimits.Default);
            historyService.EnforceRetentionAsync(DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult();

            using var context = new TrayApplicationContext(
                paths,
                settings,
                settingsStore,
                startupManager,
                historyService,
                singleInstance,
                logger,
                openResult.RecoveredCorruption);
            context.StartSecondaryInstanceListener();
            Application.Run(context);
        }
        finally
        {
            singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static void TryLogFatal(RollingFileLogger? logger, Exception exception)
    {
        if (logger is null) return;
        try
        {
            logger.WriteAsync("startup_failed", exception, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private static string GetErrorCategory(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "权限错误",
        SqliteException => "数据库错误",
        IOException => "文件错误",
        _ => "初始化错误"
    };
}
