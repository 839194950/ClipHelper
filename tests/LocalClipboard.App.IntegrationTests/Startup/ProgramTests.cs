using LocalClipboard.App;
using Microsoft.Data.Sqlite;

namespace LocalClipboard.App.IntegrationTests.Startup;

public sealed class ProgramTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "LocalClipboard.ProgramTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpenRepositoryAsync_CorruptDatabaseMovesOriginalThenRecreates()
    {
        Directory.CreateDirectory(root);
        AppPaths paths = CreatePaths();
        await File.WriteAllTextAsync(paths.Database, "not a sqlite database");

        RepositoryOpenResult result = await Program.OpenRepositoryAsync(paths, CancellationToken.None);

        Assert.True(result.RecoveredCorruption);
        Assert.Empty(await result.Repository.GetAllAsync(CancellationToken.None));
        Assert.True(File.Exists(paths.Database));
        Assert.Single(Directory.GetFiles(paths.Recovery));
        Assert.DoesNotContain(Directory.GetFiles(paths.Recovery), path => path.EndsWith("history.db", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MoveCorruptDatabaseFiles_MovesDatabaseAndSidecarsWithOneSuffix()
    {
        Directory.CreateDirectory(root);
        AppPaths paths = CreatePaths();
        await File.WriteAllTextAsync(paths.Database, "database");
        await File.WriteAllTextAsync(paths.Database + "-wal", "wal");
        await File.WriteAllTextAsync(paths.Database + "-shm", "shm");

        Program.MoveCorruptDatabaseFiles(paths, new DateTimeOffset(2026, 7, 16, 8, 9, 10, TimeSpan.Zero));

        string[] recovered = Directory.GetFiles(paths.Recovery);
        Assert.Equal(3, recovered.Length);
        Assert.All(recovered, path => Assert.EndsWith("-20260716-080910000.corrupt", path));
        Assert.False(File.Exists(paths.Database));
        Assert.False(File.Exists(paths.Database + "-wal"));
        Assert.False(File.Exists(paths.Database + "-shm"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private AppPaths CreatePaths() => new(
        root,
        Path.Combine(root, "history.db"),
        Path.Combine(root, "settings.json"),
        root,
        Path.Combine(root, "logs"),
        Path.Combine(root, "recovery"));
}
