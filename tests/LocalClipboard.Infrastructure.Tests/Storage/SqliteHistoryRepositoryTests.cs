using LocalClipboard.Core.Models;
using LocalClipboard.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace LocalClipboard.Infrastructure.Tests.Storage;

public sealed class SqliteHistoryRepositoryTests : IAsyncLifetime
{
    private readonly string testDirectory = Path.Combine(Path.GetTempPath(), "LocalClipboardTests", Guid.NewGuid().ToString());
    private SqliteHistoryRepository repository = null!;

    public Task InitializeAsync()
    {
        repository = new SqliteHistoryRepository(Path.Combine(testDirectory, "history.db"));
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task InsertAndQuery_RoundTripsTextEntry()
    {
        ClipboardEntry entry = TestEntry.Text("hello", new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        await repository.InsertAsync(entry, CancellationToken.None);
        IReadOnlyList<ClipboardEntry> result = await repository.QueryAsync(new HistoryQuery(Search: "ell"), CancellationToken.None);
        Assert.Equal(entry, Assert.Single(result));
    }

    [Fact]
    public async Task Query_FiltersTypeAndFavoritesAndOrdersNewestFirst()
    {
        DateTimeOffset now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        ClipboardEntry textFavorite = TestEntry.Text("text", now, favorite: true);
        ClipboardEntry oldFavorite = TestEntry.Image("old", now.AddMinutes(-2), favorite: true);
        ClipboardEntry newFavorite = TestEntry.Image("new", now.AddMinutes(-1), favorite: true);
        ClipboardEntry newestOrdinary = TestEntry.Image("ordinary", now);
        foreach (ClipboardEntry entry in new[] { textFavorite, oldFavorite, newFavorite, newestOrdinary })
            await repository.InsertAsync(entry, CancellationToken.None);

        IReadOnlyList<ClipboardEntry> result = await repository.QueryAsync(new HistoryQuery(ContentType: ClipboardContentType.Image, FavoritesOnly: true), CancellationToken.None);
        Assert.Equal(new[] { newFavorite.Id, oldFavorite.Id }, result.Select(entry => entry.Id));
    }

    [Fact]
    public async Task ClearAsync_ProtectsFavoritesUnlessRequested()
    {
        DateTimeOffset now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        ClipboardEntry ordinary = TestEntry.Text("ordinary", now);
        ClipboardEntry favorite = TestEntry.Text("favorite", now, favorite: true);
        await repository.InsertAsync(ordinary, CancellationToken.None);
        await repository.InsertAsync(favorite, CancellationToken.None);
        await repository.ClearAsync(includeFavorites: false, CancellationToken.None);
        Assert.Equal(favorite.Id, Assert.Single(await repository.GetAllAsync(CancellationToken.None)).Id);
        await repository.ClearAsync(includeFavorites: true, CancellationToken.None);
        Assert.Empty(await repository.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LatestTouchFavoriteAndDelete_UpdateStoredHistory()
    {
        DateTimeOffset now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        ClipboardEntry oldest = TestEntry.Text("oldest", now.AddMinutes(-2));
        ClipboardEntry newest = TestEntry.Text("newest", now.AddMinutes(-1));
        await repository.InsertAsync(oldest, CancellationToken.None);
        await repository.InsertAsync(newest, CancellationToken.None);
        Assert.Equal(newest.Id, (await repository.GetLatestAsync(CancellationToken.None))!.Id);
        await repository.TouchAsync(oldest.Id, now.AddMinutes(5), CancellationToken.None);
        Assert.Equal(oldest.Id, (await repository.GetLatestAsync(CancellationToken.None))!.Id);
        await repository.SetFavoriteAsync(oldest.Id, true, CancellationToken.None);
        Assert.True((await repository.GetAllAsync(CancellationToken.None)).Single(entry => entry.Id == oldest.Id).IsFavorite);
        await repository.DeleteAsync(oldest.Id, CancellationToken.None);
        Assert.DoesNotContain(await repository.GetAllAsync(CancellationToken.None), entry => entry.Id == oldest.Id);
    }

    [Fact]
    public async Task Query_AppliesLimitAndOffset()
    {
        DateTimeOffset now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        ClipboardEntry oldest = TestEntry.Text("oldest", now.AddMinutes(-3));
        ClipboardEntry middle = TestEntry.Text("middle", now.AddMinutes(-2));
        ClipboardEntry newest = TestEntry.Text("newest", now.AddMinutes(-1));
        foreach (ClipboardEntry entry in new[] { oldest, middle, newest })
            await repository.InsertAsync(entry, CancellationToken.None);
        IReadOnlyList<ClipboardEntry> result = await repository.QueryAsync(new HistoryQuery(Limit: 1, Offset: 1), CancellationToken.None);
        Assert.Equal(middle.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task Repository_OrdersTimestampsByInstantAcrossOffsets()
    {
        DateTimeOffset lexicallyLaterButOlder = new(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(14));
        DateTimeOffset lexicallyEarlierButNewer = new(2026, 7, 15, 9, 30, 0, TimeSpan.FromHours(-10));
        ClipboardEntry older = TestEntry.Text("older", lexicallyLaterButOlder);
        ClipboardEntry newer = TestEntry.Text("newer", lexicallyEarlierButNewer);
        await repository.InsertAsync(older, CancellationToken.None);
        await repository.InsertAsync(newer, CancellationToken.None);

        Assert.Equal(newer.Id, (await repository.GetLatestAsync(CancellationToken.None))!.Id);
        Assert.Equal(
            new[] { newer.Id, older.Id },
            (await repository.QueryAsync(new HistoryQuery(), CancellationToken.None)).Select(entry => entry.Id));

        DateTimeOffset touchedNewest = new(2026, 7, 15, 8, 0, 0, TimeSpan.FromHours(-12));
        await repository.TouchAsync(older.Id, touchedNewest, CancellationToken.None);

        Assert.Equal(older.Id, (await repository.GetLatestAsync(CancellationToken.None))!.Id);
        Assert.Equal(
            new[] { older.Id, newer.Id },
            (await repository.QueryAsync(new HistoryQuery(), CancellationToken.None)).Select(entry => entry.Id));
    }

    [Theory]
    [InlineData("%", "literal % value", "literal wildcard value")]
    [InlineData("_", "literal _ value", "literal x value")]
    [InlineData("\\", "literal \\ value", "literal slash value")]
    public async Task Query_SearchTreatsLikeMetacharactersAsLiterals(string search, string literalMatch, string wildcardDecoy)
    {
        DateTimeOffset now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        ClipboardEntry expected = TestEntry.Text(literalMatch, now.AddMinutes(-1));
        await repository.InsertAsync(expected, CancellationToken.None);
        await repository.InsertAsync(TestEntry.Text(wildcardDecoy, now), CancellationToken.None);
        IReadOnlyList<ClipboardEntry> result = await repository.QueryAsync(new HistoryQuery(Search: search), CancellationToken.None);
        Assert.Equal(expected.Id, Assert.Single(result).Id);
    }

    [Fact]
    public async Task MultipleRepositories_OpenSuccessfully()
    {
        SqliteHistoryRepository[] repositories = Enumerable.Range(0, 16)
            .Select(index => new SqliteHistoryRepository(Path.Combine(testDirectory, index.ToString(), "history.db")))
            .ToArray();
        await Task.WhenAll(repositories.Select(candidate => candidate.GetAllAsync(CancellationToken.None)));
    }

    private static class TestEntry
    {
        public static ClipboardEntry Text(string content, DateTimeOffset usedAt, bool favorite = false) => new(
            Guid.NewGuid(), ClipboardContentType.Text, content, $"hash:{content}", null, null, 0, 0,
            content.Length, usedAt.AddMinutes(-1), usedAt, favorite);

        public static ClipboardEntry Image(string content, DateTimeOffset usedAt, bool favorite = false) => new(
            Guid.NewGuid(), ClipboardContentType.Image, null, $"hash:{content}", $"images/{content}.png",
            $"thumbnails/{content}.png", 640, 480, 12345, usedAt.AddMinutes(-1), usedAt, favorite);
    }
}
