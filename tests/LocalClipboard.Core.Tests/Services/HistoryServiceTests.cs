using LocalClipboard.Core.Abstractions;
using LocalClipboard.Core.Models;
using LocalClipboard.Core.Services;

namespace LocalClipboard.Core.Tests.Services;

public sealed class HistoryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CaptureAsync_ConsecutiveDuplicateText_TouchesLatestInsteadOfInserting()
    {
        var repository = new FakeHistoryRepository();
        var service = CreateService(repository);
        var capture = new ClipboardCapture(ClipboardContentType.Text, "same", null, 0, 0, Now);

        var first = await service.CaptureAsync(capture, CancellationToken.None);
        var second = await service.CaptureAsync(capture with { CapturedAt = Now.AddMinutes(1) }, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(Now.AddMinutes(1), second.LastUsedAt);
        Assert.Single(repository.Inserted);
        Assert.Equal((first!.Id, Now.AddMinutes(1)), repository.Touches.Single());
    }

    [Fact]
    public async Task CaptureAsync_RejectsOversizedImageWithoutSaving()
    {
        var repository = new FakeHistoryRepository();
        var imageStore = new FakeImageStore();
        var service = CreateService(repository, imageStore, new RetentionLimits(10, TimeSpan.FromDays(30), 1_000, 2));
        repository.Entries.Add(Entry(ContentHasher.HashBytes(new byte[] { 1, 2, 3 }), Now.AddMinutes(-1), "existing.png"));

        var result = await service.CaptureAsync(
            new ClipboardCapture(ClipboardContentType.Image, null, [1, 2, 3], 2, 2, Now),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(imageStore.SaveCalls);
        Assert.Empty(repository.Inserted);
        Assert.Empty(repository.Touches);
    }

    [Fact]
    public async Task CaptureAsync_WhenImageSavedButInsertFails_DeletesBothFilesAndRethrows()
    {
        var repository = new FakeHistoryRepository { ThrowOnInsert = new InvalidOperationException("insert failed") };
        var imageStore = new FakeImageStore();
        var service = CreateService(repository, imageStore);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(
            new ClipboardCapture(ClipboardContentType.Image, null, [1, 2], 2, 1, Now),
            CancellationToken.None));

        Assert.Equal("insert failed", exception.Message);
        var save = Assert.Single(imageStore.SaveCalls);
        Assert.Equal((save.ImagePath, save.ThumbnailPath), Assert.Single(imageStore.DeletedImages));
    }

    [Fact]
    public async Task CaptureAsync_WhenInsertAndCleanupFail_ThrowsAggregateAndUsesNonCancelableCleanupToken()
    {
        using var cancellation = new CancellationTokenSource();
        var databaseException = new InvalidOperationException("db");
        var cleanupException = new IOException("cleanup");
        var repository = new FakeHistoryRepository
        {
            ThrowOnInsert = databaseException,
            CancelOnInsertFailure = cancellation,
        };
        var imageStore = new FakeImageStore { DeleteException = cleanupException };
        var service = CreateService(repository, imageStore);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => service.CaptureAsync(
            new ClipboardCapture(ClipboardContentType.Image, null, [1, 2], 2, 1, Now),
            cancellation.Token));

        Assert.Equal(new Exception[] { databaseException, cleanupException }, exception.InnerExceptions);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(Assert.Single(imageStore.DeleteTokens).CanBeCanceled);
    }

    [Fact]
    public async Task CaptureAsync_CreatesCompleteTextAndImageEntries()
    {
        var repository = new FakeHistoryRepository();
        var imageStore = new FakeImageStore();
        var service = CreateService(repository, imageStore);

        var text = await service.CaptureAsync(
            new ClipboardCapture(ClipboardContentType.Text, "hello", null, 0, 0, Now),
            CancellationToken.None);
        var bytes = new byte[] { 1, 2, 3 };
        var image = await service.CaptureAsync(
            new ClipboardCapture(ClipboardContentType.Image, null, bytes, 3, 4, Now.AddMinutes(1)),
            CancellationToken.None);

        Assert.NotNull(text);
        Assert.Equal(ContentHasher.HashText("hello"), text!.ContentHash);
        Assert.Equal("hello", text.TextContent);
        Assert.Equal(Now, text.CreatedAt);
        Assert.Equal(Now, text.LastUsedAt);

        Assert.NotNull(image);
        Assert.Equal(ContentHasher.HashBytes(bytes), image!.ContentHash);
        Assert.Equal(image.Id, Assert.Single(imageStore.SaveCalls).EntryId);
        Assert.Equal(imageStore.SaveCalls[0].ImagePath, image.ImagePath);
        Assert.Equal(imageStore.SaveCalls[0].ThumbnailPath, image.ThumbnailPath);
        Assert.Equal(3, image.Width);
        Assert.Equal(4, image.Height);
        Assert.Equal(bytes.Length, image.EncodedSize);
        Assert.Equal(Now.AddMinutes(1), image.CreatedAt);
        Assert.False(image.IsFavorite);
    }

    [Fact]
    public async Task CaptureAsync_AfterInsert_EnforcesRetentionAndDeletesOldEntryImage()
    {
        var repository = new FakeHistoryRepository();
        var imageStore = new FakeImageStore();
        var service = CreateService(repository, imageStore, new RetentionLimits(1, TimeSpan.FromDays(30), 1_000, 1_000));
        var oldEntry = Entry("old", Now.AddMinutes(-1), "old.png");
        repository.Entries.Add(oldEntry);

        var captured = await service.CaptureAsync(
            new ClipboardCapture(ClipboardContentType.Text, "new", null, 0, 0, Now),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.DoesNotContain(repository.Entries, entry => entry.Id == oldEntry.Id);
        Assert.Contains(repository.Entries, entry => entry.Id == captured.Id);
        Assert.Equal((oldEntry.ImagePath, oldEntry.ThumbnailPath), Assert.Single(imageStore.DeletedImages));
    }

    [Fact]
    public async Task CaptureAsync_NonConsecutiveDuplicate_InsertsNewEntry()
    {
        var repository = new FakeHistoryRepository();
        var service = CreateService(repository);

        await service.CaptureAsync(new ClipboardCapture(ClipboardContentType.Text, "A", null, 0, 0, Now), CancellationToken.None);
        await service.CaptureAsync(new ClipboardCapture(ClipboardContentType.Text, "B", null, 0, 0, Now.AddMinutes(1)), CancellationToken.None);
        await service.CaptureAsync(new ClipboardCapture(ClipboardContentType.Text, "A", null, 0, 0, Now.AddMinutes(2)), CancellationToken.None);

        Assert.Equal(3, repository.Inserted.Count);
        Assert.Empty(repository.Touches);
        Assert.Equal(new[] { "A", "B", "A" }, repository.Entries.Select(entry => entry.TextContent));
    }

    [Fact]
    public async Task CaptureAsync_ConcurrentIdenticalCaptures_InsertOnceAndTouchSecond()
    {
        var repository = new FakeHistoryRepository { PauseFirstGetLatest = true };
        var service = CreateService(repository);
        var capture = new ClipboardCapture(ClipboardContentType.Text, "same", null, 0, 0, Now);

        var firstTask = service.CaptureAsync(capture, CancellationToken.None);
        await repository.FirstGetLatestEntered;
        var secondTask = service.CaptureAsync(capture with { CapturedAt = Now.AddMinutes(1) }, CancellationToken.None);
        SpinWait.SpinUntil(() => repository.GetLatestCallCount >= 2, TimeSpan.FromMilliseconds(100));
        repository.ReleaseFirstGetLatest();
        await Task.WhenAll(firstTask, secondTask);

        Assert.Single(repository.Entries);
        var inserted = Assert.Single(repository.Inserted);
        Assert.Equal(Now, inserted.CreatedAt);
        Assert.Equal((inserted.Id, Now.AddMinutes(1)), Assert.Single(repository.Touches));
        Assert.Equal(Now.AddMinutes(1), repository.Entries[0].LastUsedAt);
    }

    [Fact]
    public async Task CaptureAsync_OlderConsecutiveDuplicate_DoesNotMoveLastUsedAtBackward()
    {
        var repository = new FakeHistoryRepository();
        var latest = Entry(ContentHasher.HashText("same"), Now, null);
        repository.Entries.Add(latest);
        var service = CreateService(repository);

        var result = await service.CaptureAsync(
            new ClipboardCapture(ClipboardContentType.Text, "same", null, 0, 0, Now.AddMinutes(-1)),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(Now, result!.LastUsedAt);
        Assert.Equal((latest.Id, Now), Assert.Single(repository.Touches));
    }

    [Fact]
    public async Task EnforceRetentionAsync_DeletesPolicyEntriesAndTheirImages()
    {
        var repository = new FakeHistoryRepository();
        var imageStore = new FakeImageStore();
        var service = CreateService(repository, imageStore, new RetentionLimits(1, TimeSpan.FromDays(30), 1_000, 1_000));
        var oldest = Entry("oldest", Now.AddMinutes(-2), "oldest.png");
        var newest = Entry("newest", Now.AddMinutes(-1), "newest.png");
        repository.Entries.Add(oldest);
        repository.Entries.Add(newest);

        await service.EnforceRetentionAsync(Now, CancellationToken.None);

        Assert.Equal(new[] { oldest.Id }, repository.DeletedIds);
        Assert.Equal((oldest.ImagePath, oldest.ThumbnailPath), Assert.Single(imageStore.DeletedImages));
    }

    [Fact]
    public async Task QueryAndMutationMethods_DelegateAndCleanUpImages()
    {
        var repository = new FakeHistoryRepository();
        var imageStore = new FakeImageStore();
        var service = CreateService(repository, imageStore);
        var image = Entry("image", Now, "image.png");
        var text = Entry("text", Now, null);
        repository.Entries.Add(image);
        repository.Entries.Add(text);

        var queried = await service.QueryAsync(new HistoryQuery(ContentType: ClipboardContentType.Image), CancellationToken.None);
        await service.SetFavoriteAsync(image.Id, true, CancellationToken.None);
        await service.DeleteAsync(image, CancellationToken.None);
        await service.ClearAsync(includeFavorites: false, CancellationToken.None);

        Assert.Equal(new[] { image.Id }, queried.Select(entry => entry.Id));
        Assert.Equal((image.Id, true), Assert.Single(repository.FavoriteChanges));
        Assert.Equal(new[] { image.Id, text.Id }, repository.DeletedIds);
        Assert.Equal(
            new[] { (image.ImagePath, image.ThumbnailPath), (text.ImagePath, text.ThumbnailPath) },
            imageStore.DeletedImages);
    }

    [Fact]
    public async Task ClearAsync_IncludeFavoritesControlsWhichImagesAndRecordsAreCleared()
    {
        var repository = new FakeHistoryRepository();
        var imageStore = new FakeImageStore();
        var service = CreateService(repository, imageStore);
        var favorite = Entry("favorite", Now, "favorite.png", isFavorite: true);
        var ordinary = Entry("ordinary", Now, "ordinary.png");
        repository.Entries.Add(favorite);
        repository.Entries.Add(ordinary);

        await service.ClearAsync(includeFavorites: false, CancellationToken.None);

        Assert.Equal(new[] { ordinary.Id }, repository.DeletedIds);
        Assert.Equal(new[] { (ordinary.ImagePath, ordinary.ThumbnailPath) }, imageStore.DeletedImages);
        Assert.Contains(favorite, repository.Entries);

        await service.ClearAsync(includeFavorites: true, CancellationToken.None);

        Assert.Equal(new[] { ordinary.Id, favorite.Id }, repository.DeletedIds);
        Assert.Equal(
            new[] { (ordinary.ImagePath, ordinary.ThumbnailPath), (favorite.ImagePath, favorite.ThumbnailPath) },
            imageStore.DeletedImages);
    }

    [Fact]
    public async Task ClearAsync_WhenFirstImageCleanupFails_ContinuesAndAggregatesCleanupFailures()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new FakeHistoryRepository();
        var first = Entry("first", Now.AddMinutes(-2), "first.png");
        var second = Entry("second", Now.AddMinutes(-1), "second.png");
        repository.Entries.Add(first);
        repository.Entries.Add(second);
        var cleanupException = new IOException("first cleanup");
        var imageStore = new FakeImageStore();
        imageStore.DeleteExceptionsByImagePath.Add(first.ImagePath!, cleanupException);
        var service = CreateService(repository, imageStore);

        var exception = await Assert.ThrowsAsync<AggregateException>(() => service.ClearAsync(
            includeFavorites: true,
            cancellation.Token));

        Assert.Equal(new[] { cleanupException }, exception.InnerExceptions);
        Assert.Equal(new[] { first.Id, second.Id }, repository.DeletedIds);
        Assert.Equal(
            new[] { (first.ImagePath, first.ThumbnailPath), (second.ImagePath, second.ThumbnailPath) },
            imageStore.DeletedImages);
        Assert.All(imageStore.DeleteTokens, token => Assert.False(token.CanBeCanceled));
    }

    private static HistoryService CreateService(
        FakeHistoryRepository repository,
        FakeImageStore? imageStore = null,
        RetentionLimits? limits = null)
    {
        return new HistoryService(repository, imageStore ?? new FakeImageStore(), limits ?? new RetentionLimits(100, TimeSpan.FromDays(30), 1_000, 1_000));
    }

    private static ClipboardEntry Entry(string hash, DateTimeOffset lastUsedAt, string? imagePath, bool isFavorite = false)
    {
        return new ClipboardEntry(Guid.NewGuid(), imagePath is null ? ClipboardContentType.Text : ClipboardContentType.Image, imagePath is null ? hash : null, hash, imagePath, imagePath is null ? null : imagePath + ".thumb", imagePath is null ? 0 : 1, imagePath is null ? 0 : 1, imagePath is null ? 0 : 10, lastUsedAt, lastUsedAt, isFavorite);
    }

    private sealed class FakeHistoryRepository : IHistoryRepository
    {
        public List<ClipboardEntry> Entries { get; } = [];
        public List<ClipboardEntry> Inserted { get; } = [];
        public List<(Guid Id, DateTimeOffset UsedAt)> Touches { get; } = [];
        public List<(Guid Id, bool IsFavorite)> FavoriteChanges { get; } = [];
        public List<Guid> DeletedIds { get; } = [];
        public Exception? ThrowOnInsert { get; init; }
        public CancellationTokenSource? CancelOnInsertFailure { get; init; }
        public bool PauseFirstGetLatest { get; init; }
        public int GetLatestCallCount => getLatestCallCount;
        public Task FirstGetLatestEntered => firstGetLatestEntered.Task;

        private readonly TaskCompletionSource firstGetLatestEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstGetLatest = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int getLatestCallCount;

        public async Task<ClipboardEntry?> GetLatestAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref getLatestCallCount);
            if (PauseFirstGetLatest && call == 1)
            {
                firstGetLatestEntered.SetResult();
                await releaseFirstGetLatest.Task.WaitAsync(cancellationToken);
            }

            lock (Entries) return Entries.OrderByDescending(entry => entry.LastUsedAt).FirstOrDefault();
        }
        public void ReleaseFirstGetLatest() => releaseFirstGetLatest.TrySetResult();
        public Task<IReadOnlyList<ClipboardEntry>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClipboardEntry>>(Entries.OrderBy(entry => entry.LastUsedAt).ToList());
        public Task<IReadOnlyList<ClipboardEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken)
        {
            IEnumerable<ClipboardEntry> result = Entries;
            if (!string.IsNullOrWhiteSpace(query.Search)) result = result.Where(entry => entry.TextContent?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true);
            if (query.ContentType is not null) result = result.Where(entry => entry.ContentType == query.ContentType);
            if (query.FavoritesOnly) result = result.Where(entry => entry.IsFavorite);
            return Task.FromResult<IReadOnlyList<ClipboardEntry>>(result.OrderByDescending(entry => entry.LastUsedAt).Skip(query.Offset).Take(query.Limit).ToList());
        }
        public Task InsertAsync(ClipboardEntry entry, CancellationToken cancellationToken)
        {
            if (ThrowOnInsert is not null)
            {
                CancelOnInsertFailure?.Cancel();
                throw ThrowOnInsert;
            }
            lock (Entries)
            {
                Entries.Add(entry);
                Inserted.Add(entry);
            }
            return Task.CompletedTask;
        }
        public Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken)
        {
            var index = Entries.FindIndex(entry => entry.Id == id);
            Entries[index] = Entries[index] with { LastUsedAt = usedAt };
            Touches.Add((id, usedAt));
            return Task.CompletedTask;
        }
        public Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken)
        {
            var index = Entries.FindIndex(entry => entry.Id == id);
            Entries[index] = Entries[index] with { IsFavorite = isFavorite };
            FavoriteChanges.Add((id, isFavorite));
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(entry => entry.Id == id);
            DeletedIds.Add(id);
            return Task.CompletedTask;
        }
        public Task ClearAsync(bool includeFavorites, CancellationToken cancellationToken)
        {
            var ids = Entries.Where(entry => includeFavorites || !entry.IsFavorite).Select(entry => entry.Id).ToList();
            foreach (var id in ids) Entries.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeImageStore : IImageStore
    {
        public List<(Guid EntryId, string Hash, byte[] PngBytes, int Width, int Height, string ImagePath, string ThumbnailPath)> SaveCalls { get; } = [];
        public List<(string? ImagePath, string? ThumbnailPath)> DeletedImages { get; } = [];
        public List<CancellationToken> DeleteTokens { get; } = [];
        public Dictionary<string, Exception> DeleteExceptionsByImagePath { get; } = [];
        public Exception? DeleteException { get; init; }

        public Task<StoredImage> SaveAsync(Guid entryId, string hash, byte[] pngBytes, int width, int height, CancellationToken cancellationToken)
        {
            var imagePath = $"images/{entryId}.png";
            var thumbnailPath = $"images/{entryId}.thumb.png";
            SaveCalls.Add((entryId, hash, pngBytes, width, height, imagePath, thumbnailPath));
            return Task.FromResult(new StoredImage(imagePath, thumbnailPath, width, height, pngBytes.Length));
        }
        public Task DeleteAsync(string? imagePath, string? thumbnailPath, CancellationToken cancellationToken)
        {
            DeletedImages.Add((imagePath, thumbnailPath));
            DeleteTokens.Add(cancellationToken);
            if (imagePath is not null && DeleteExceptionsByImagePath.TryGetValue(imagePath, out var pathException)) throw pathException;
            if (DeleteException is not null) throw DeleteException;
            return Task.CompletedTask;
        }
        public Task DeleteOrphansAsync(IReadOnlySet<string> referencedPaths, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
