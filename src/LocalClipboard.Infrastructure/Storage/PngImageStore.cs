using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LocalClipboard.Core.Abstractions;

namespace LocalClipboard.Infrastructure.Storage;

public sealed class PngImageStore : IImageStore
{
    private const int ThumbnailMaxWidth = 320;
    private const int ThumbnailMaxHeight = 220;
    private readonly string rootPath;
    private readonly string rootPathWithSeparator;

    public PngImageStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        this.rootPath = Path.GetFullPath(rootPath);
        rootPathWithSeparator = Path.EndsInDirectorySeparator(this.rootPath)
            ? this.rootPath
            : this.rootPath + Path.DirectorySeparatorChar;
    }

    public async Task<StoredImage> SaveAsync(
        Guid entryId,
        string hash,
        byte[] pngBytes,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        ValidateHash(hash);
        ArgumentNullException.ThrowIfNull(pngBytes);
        cancellationToken.ThrowIfCancellationRequested();

        string baseName = $"{entryId:N}-{hash[..12]}";
        string imagePath = $"images/{baseName}.png";
        string thumbnailPath = $"thumbnails/{baseName}.png";
        string imageDestination = ResolveRelativePath(imagePath);
        string thumbnailDestination = ResolveRelativePath(thumbnailPath);
        string imageTemp = ResolveRelativePath($"images/{baseName}.tmp");
        string thumbnailTemp = ResolveRelativePath($"thumbnails/{baseName}.tmp");
        bool imageMoved = false;
        bool thumbnailMoved = false;

        Directory.CreateDirectory(Path.GetDirectoryName(imageDestination)!);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnailDestination)!);

        try
        {
            await File.WriteAllBytesAsync(imageTemp, pngBytes, cancellationToken);

            using var sourceStream = new MemoryStream(pngBytes, writable: false);
            using Image source = Image.FromStream(sourceStream, useEmbeddedColorManagement: false, validateImageData: true);
            if (source.RawFormat.Guid != ImageFormat.Png.Guid)
                throw new ArgumentException("Image data must be PNG encoded.", nameof(pngBytes));

            (int thumbnailWidth, int thumbnailHeight) = GetThumbnailSize(source.Width, source.Height);
            using var thumbnail = new Bitmap(thumbnailWidth, thumbnailHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(thumbnail))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, new Rectangle(0, 0, thumbnailWidth, thumbnailHeight));
            }

            thumbnail.Save(thumbnailTemp, ImageFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(imageTemp, imageDestination, overwrite: true);
            imageMoved = true;
            File.Move(thumbnailTemp, thumbnailDestination, overwrite: true);
            thumbnailMoved = true;

            return new StoredImage(imagePath, thumbnailPath, width, height, pngBytes.LongLength);
        }
        catch
        {
            TryDeleteFile(imageTemp);
            TryDeleteFile(thumbnailTemp);
            if (imageMoved) TryDeleteFile(imageDestination);
            if (thumbnailMoved) TryDeleteFile(thumbnailDestination);
            throw;
        }
    }

    public Task DeleteAsync(string? imagePath, string? thumbnailPath, CancellationToken cancellationToken)
    {
        List<string> resolvedPaths = [];
        foreach (string? relativePath in new[] { imagePath, thumbnailPath })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (relativePath is not null) resolvedPaths.Add(ResolveRelativePath(relativePath));
        }

        foreach (string resolvedPath in resolvedPaths) File.Delete(resolvedPath);

        return Task.CompletedTask;
    }

    public Task DeleteOrphansAsync(IReadOnlySet<string> referencedPaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(referencedPaths);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedReferences = new HashSet<string>(
            referencedPaths.Select(NormalizeRelativePath),
            StringComparer.OrdinalIgnoreCase);

        foreach (string directoryName in new[] { "images", "thumbnails" })
        {
            string directoryPath = ResolveRelativePath(directoryName);
            if (!Directory.Exists(directoryPath)) continue;

            foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*.png", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, filePath));
                if (!normalizedReferences.Contains(relativePath)) File.Delete(filePath);
            }
        }

        return Task.CompletedTask;
    }

    private string ResolveRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Path must be relative to the image store root.", nameof(relativePath));

        string[] segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            throw new ArgumentException("Parent path segments are not allowed.", nameof(relativePath));

        string resolvedPath = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolvedPath.StartsWith(rootPathWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path resolves outside the image store root.", nameof(relativePath));

        return resolvedPath;
    }

    private static (int Width, int Height) GetThumbnailSize(int width, int height)
    {
        double scale = Math.Min(1d, Math.Min((double)ThumbnailMaxWidth / width, (double)ThumbnailMaxHeight / height));
        int scaledWidth = Math.Max(1, Math.Min(ThumbnailMaxWidth, (int)Math.Round(width * scale)));
        int scaledHeight = Math.Max(1, Math.Min(ThumbnailMaxHeight, (int)Math.Round(height * scale)));
        return (scaledWidth, scaledHeight);
    }

    private static string NormalizeRelativePath(string relativePath) => relativePath.Replace('\\', '/');

    private static void ValidateHash(string hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Hash must contain exactly 64 lowercase hexadecimal characters.", nameof(hash));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
