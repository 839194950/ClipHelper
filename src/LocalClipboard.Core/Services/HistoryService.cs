using LocalClipboard.Core.Abstractions;
using LocalClipboard.Core.Models;

namespace LocalClipboard.Core.Services;

public sealed class HistoryService
{
    private readonly IHistoryRepository repository;
    private readonly IImageStore imageStore;
    private readonly RetentionLimits limits;

    public HistoryService(IHistoryRepository repository, IImageStore imageStore, RetentionLimits limits)
    {
        this.repository = repository;
        this.imageStore = imageStore;
        this.limits = limits;
    }

    public async Task<ClipboardEntry?> CaptureAsync(ClipboardCapture capture, CancellationToken cancellationToken)
    {
        var isText = capture.ContentType == ClipboardContentType.Text;
        if (isText && string.IsNullOrEmpty(capture.Text)) return null;
        if (!isText && (capture.PngBytes is null || capture.PngBytes.Length == 0)) return null;
        if (!isText && capture.PngBytes!.LongLength > limits.MaximumSingleImageBytes) return null;

        var hash = isText ? ContentHasher.HashText(capture.Text!) : ContentHasher.HashBytes(capture.PngBytes!);
        var latest = await repository.GetLatestAsync(cancellationToken);
        if (latest is not null && latest.ContentType == capture.ContentType && latest.ContentHash == hash)
        {
            await repository.TouchAsync(latest.Id, capture.CapturedAt, cancellationToken);
            return latest with { LastUsedAt = capture.CapturedAt };
        }

        var entryId = Guid.NewGuid();
        StoredImage? storedImage = null;
        if (!isText) storedImage = await imageStore.SaveAsync(entryId, hash, capture.PngBytes!, capture.Width, capture.Height, cancellationToken);

        var entry = new ClipboardEntry(entryId, capture.ContentType, isText ? capture.Text : null, hash, storedImage?.ImagePath, storedImage?.ThumbnailPath, storedImage?.Width ?? 0, storedImage?.Height ?? 0, storedImage?.EncodedSize ?? 0, capture.CapturedAt, capture.CapturedAt, false);
        try
        {
            await repository.InsertAsync(entry, cancellationToken);
        }
        catch
        {
            if (storedImage is not null) await imageStore.DeleteAsync(storedImage.ImagePath, storedImage.ThumbnailPath, cancellationToken);
            throw;
        }

        await EnforceRetentionAsync(capture.CapturedAt, cancellationToken);
        return entry;
    }

    public Task<IReadOnlyList<ClipboardEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken) => repository.QueryAsync(query, cancellationToken);
    public Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken) => repository.SetFavoriteAsync(id, isFavorite, cancellationToken);

    public async Task DeleteAsync(ClipboardEntry entry, CancellationToken cancellationToken)
    {
        await repository.DeleteAsync(entry.Id, cancellationToken);
        await imageStore.DeleteAsync(entry.ImagePath, entry.ThumbnailPath, cancellationToken);
    }

    public async Task ClearAsync(bool includeFavorites, CancellationToken cancellationToken)
    {
        var entries = await repository.GetAllAsync(cancellationToken);
        foreach (var entry in entries.Where(entry => includeFavorites || !entry.IsFavorite).ToList())
        {
            await repository.DeleteAsync(entry.Id, cancellationToken);
            await imageStore.DeleteAsync(entry.ImagePath, entry.ThumbnailPath, cancellationToken);
        }
    }

    public async Task EnforceRetentionAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entries = await repository.GetAllAsync(cancellationToken);
        var ids = RetentionPolicy.SelectForDeletion(entries, now, limits);
        foreach (var entry in entries.Where(entry => ids.Contains(entry.Id)))
        {
            await repository.DeleteAsync(entry.Id, cancellationToken);
            await imageStore.DeleteAsync(entry.ImagePath, entry.ThumbnailPath, cancellationToken);
        }
    }
}
