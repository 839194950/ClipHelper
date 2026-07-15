namespace LocalClipboard.Core.Abstractions;

public sealed record StoredImage(string ImagePath, string ThumbnailPath, int Width, int Height, long EncodedSize);

public interface IImageStore
{
    Task<StoredImage> SaveAsync(Guid entryId, string hash, byte[] pngBytes, int width, int height, CancellationToken cancellationToken);
    Task DeleteAsync(string? imagePath, string? thumbnailPath, CancellationToken cancellationToken);
    Task DeleteOrphansAsync(IReadOnlySet<string> referencedPaths, CancellationToken cancellationToken);
}
