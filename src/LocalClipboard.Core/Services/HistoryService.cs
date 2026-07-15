using LocalClipboard.Core.Abstractions;
using LocalClipboard.Core.Models;

namespace LocalClipboard.Core.Services;

public sealed class HistoryService
{
    private readonly IHistoryRepository repository;
    private readonly IImageStore imageStore;
    private readonly RetentionLimits limits;
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    public HistoryService(IHistoryRepository repository, IImageStore imageStore, RetentionLimits limits)
    {
        this.repository = repository;
        this.imageStore = imageStore;
        this.limits = limits;
    }

    public async Task<ClipboardEntry?> CaptureAsync(ClipboardCapture capture, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await CaptureCoreAsync(capture, cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task<ClipboardEntry?> CaptureCoreAsync(ClipboardCapture capture, CancellationToken cancellationToken)
    {
        var isText = capture.ContentType == ClipboardContentType.Text;
        if (isText && string.IsNullOrEmpty(capture.Text)) return null;
        if (!isText && (capture.PngBytes is null || capture.PngBytes.Length == 0)) return null;
        if (!isText && capture.PngBytes!.LongLength > limits.MaximumSingleImageBytes) return null;

        var hash = isText ? ContentHasher.HashText(capture.Text!) : ContentHasher.HashBytes(capture.PngBytes!);
        var latest = await repository.GetLatestAsync(cancellationToken);
        if (latest is not null && latest.ContentType == capture.ContentType && latest.ContentHash == hash)
        {
            var usedAt = latest.LastUsedAt >= capture.CapturedAt ? latest.LastUsedAt : capture.CapturedAt;
            await repository.TouchAsync(latest.Id, usedAt, cancellationToken);
            return latest with { LastUsedAt = usedAt };
        }

        var entryId = Guid.NewGuid();
        StoredImage? storedImage = null;
        if (!isText) storedImage = await imageStore.SaveAsync(entryId, hash, capture.PngBytes!, capture.Width, capture.Height, cancellationToken);

        var entry = new ClipboardEntry(entryId, capture.ContentType, isText ? capture.Text : null, hash, storedImage?.ImagePath, storedImage?.ThumbnailPath, storedImage?.Width ?? 0, storedImage?.Height ?? 0, storedImage?.EncodedSize ?? 0, capture.CapturedAt, capture.CapturedAt, false);
        try
        {
            await repository.InsertAsync(entry, cancellationToken);
        }
        catch (Exception insertException)
        {
            if (storedImage is not null)
            {
                try
                {
                    await imageStore.DeleteAsync(storedImage.ImagePath, storedImage.ThumbnailPath, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(insertException, cleanupException);
                }
            }

            throw;
        }

        await EnforceRetentionCoreAsync(capture.CapturedAt, cancellationToken);
        return entry;
    }

    public Task<IReadOnlyList<ClipboardEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) => repository.QueryAsync(query, cancellationToken);
    public async Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            await repository.SetFavoriteAsync(id, isFavorite, cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task DeleteAsync(ClipboardEntry entry, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            await DeleteCoreAsync(entry, cancellationToken, null);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task ClearAsync(bool includeFavorites, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            var entries = await repository.GetAllAsync(cancellationToken);
            await DeleteBatchCoreAsync(entries.Where(entry => includeFavorites || !entry.IsFavorite).ToList(), cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    public async Task EnforceRetentionAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            await EnforceRetentionCoreAsync(now, cancellationToken);
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async Task EnforceRetentionCoreAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entries = await repository.GetAllAsync(cancellationToken);
        var ids = RetentionPolicy.SelectForDeletion(entries, now, limits);
        await DeleteBatchCoreAsync(entries.Where(entry => ids.Contains(entry.Id)).ToList(), cancellationToken);
    }

    private async Task DeleteBatchCoreAsync(IReadOnlyList<ClipboardEntry> entries, CancellationToken cancellationToken)
    {
        var cleanupFailures = new List<Exception>();
        foreach (var entry in entries)
        {
            await DeleteCoreAsync(entry, cancellationToken, cleanupFailures);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(cleanupFailures);
        }
    }

    private async Task DeleteCoreAsync(ClipboardEntry entry, CancellationToken cancellationToken, List<Exception>? cleanupFailures)
    {
        await repository.DeleteAsync(entry.Id, cancellationToken);
        try
        {
            await imageStore.DeleteAsync(entry.ImagePath, entry.ThumbnailPath, cleanupFailures is null ? cancellationToken : CancellationToken.None);
        }
        catch (Exception exception) when (cleanupFailures is not null)
        {
            cleanupFailures.Add(exception);
        }
    }
}
