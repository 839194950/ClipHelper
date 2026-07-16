using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LocalClipboard.Core.Abstractions;

namespace LocalClipboard.Infrastructure.Storage;

public sealed class PngImageStore : IImageStore
{
    private const int ThumbnailMaxWidth = 320;
    private const int ThumbnailMaxHeight = 220;
    private static readonly SemaphoreSlim SaveGate = new(1, 1);
    private readonly string rootPath;
    private readonly string rootPathWithSeparator;

    public PngImageStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        this.rootPath = Path.GetFullPath(rootPath);
        rootPathWithSeparator = Path.EndsInDirectorySeparator(this.rootPath)
            ? this.rootPath
            : this.rootPath + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(this.rootPath);
        EnsureDirectoryIsSafe(this.rootPath);
        Directory.CreateDirectory(Path.Combine(this.rootPath, "images"));
        Directory.CreateDirectory(Path.Combine(this.rootPath, "thumbnails"));
        ValidateStorageDirectories();
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
        ValidateStorageDirectories();

        string baseName = $"{entryId:N}-{hash[..12]}";
        string imagePath = $"images/{baseName}.png";
        string thumbnailPath = $"thumbnails/{baseName}.png";
        string imageDestination = ResolveRelativePath(imagePath);
        string thumbnailDestination = ResolveRelativePath(thumbnailPath);
        string nonce = Guid.NewGuid().ToString("N");
        string imageTemp = ResolveRelativePath($"images/{baseName}-{nonce}.tmp");
        string thumbnailTemp = ResolveRelativePath($"thumbnails/{baseName}-{nonce}.tmp");
        bool imageCreated = false;
        bool thumbnailCreated = false;

        await SaveGate.WaitAsync(cancellationToken);

        try
        {
            ValidateStorageDirectories();
            using var sourceStream = new MemoryStream(pngBytes, writable: false);
            using Image source = Image.FromStream(sourceStream, useEmbeddedColorManagement: false, validateImageData: true);
            if (source.RawFormat.Guid != ImageFormat.Png.Guid)
                throw new ArgumentException("Image data must be PNG encoded.", nameof(pngBytes));

            ValidateDimensions(width, height, source.Width, source.Height);
            int actualWidth = source.Width;
            int actualHeight = source.Height;
            bool imageExists = File.Exists(imageDestination);
            bool thumbnailExists = File.Exists(thumbnailDestination);
            if (imageExists && thumbnailExists)
                return new StoredImage(imagePath, thumbnailPath, actualWidth, actualHeight, pngBytes.LongLength);
            if (imageExists || thumbnailExists)
                throw new IOException($"Stored image pair is incomplete for '{baseName}'.");

            await File.WriteAllBytesAsync(imageTemp, pngBytes, cancellationToken);

            (int thumbnailWidth, int thumbnailHeight) = GetThumbnailSize(actualWidth, actualHeight);
            using var thumbnail = new Bitmap(thumbnailWidth, thumbnailHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(thumbnail))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, new Rectangle(0, 0, thumbnailWidth, thumbnailHeight));
            }

            thumbnail.Save(thumbnailTemp, ImageFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();

            File.Move(imageTemp, imageDestination, overwrite: false);
            imageCreated = true;
            File.Move(thumbnailTemp, thumbnailDestination, overwrite: false);
            thumbnailCreated = true;

            return new StoredImage(imagePath, thumbnailPath, actualWidth, actualHeight, pngBytes.LongLength);
        }
        catch
        {
            TryDeleteFile(imageTemp);
            TryDeleteFile(thumbnailTemp);
            if (imageCreated) TryDeleteFile(imageDestination);
            if (thumbnailCreated) TryDeleteFile(thumbnailDestination);
            throw;
        }
        finally
        {
            SaveGate.Release();
        }
    }

    public Task DeleteAsync(string? imagePath, string? thumbnailPath, CancellationToken cancellationToken)
    {
        ValidateStorageDirectories();
        List<string> resolvedPaths = [];
        foreach (string? relativePath in new[] { imagePath, thumbnailPath })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (relativePath is not null) resolvedPaths.Add(ResolveRelativePath(relativePath));
        }

        foreach (string resolvedPath in resolvedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureExistingDirectoryComponentsAreSafe(resolvedPath);
            File.Delete(resolvedPath);
        }

        return Task.CompletedTask;
    }

    public Task DeleteOrphansAsync(IReadOnlySet<string> referencedPaths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(referencedPaths);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateStorageDirectories();
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
                if (!normalizedReferences.Contains(relativePath))
                {
                    EnsureExistingDirectoryComponentsAreSafe(filePath);
                    File.Delete(filePath);
                }
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

        EnsureExistingDirectoryComponentsAreSafe(resolvedPath);
        return resolvedPath;
    }

    private void ValidateStorageDirectories()
    {
        EnsureDirectoryIsSafe(rootPath);
        EnsureDirectoryIsSafe(Path.Combine(rootPath, "images"));
        EnsureDirectoryIsSafe(Path.Combine(rootPath, "thumbnails"));
    }

    private void EnsureExistingDirectoryComponentsAreSafe(string resolvedPath)
    {
        EnsureDirectoryIsSafe(rootPath);
        string relativePath = Path.GetRelativePath(rootPath, resolvedPath);
        if (relativePath == ".") return;

        string currentPath = rootPath;
        foreach (string segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!Directory.Exists(currentPath)) break;
            EnsureDirectoryIsSafe(currentPath);
        }
    }

    private static void EnsureDirectoryIsSafe(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Required image storage directory was not found: '{directoryPath}'.");
        if ((File.GetAttributes(directoryPath) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Image storage directories must not be reparse points: '{directoryPath}'.");
    }

    private static (int Width, int Height) GetThumbnailSize(int width, int height)
    {
        double scale = Math.Min(1d, Math.Min((double)ThumbnailMaxWidth / width, (double)ThumbnailMaxHeight / height));
        int scaledWidth = Math.Max(1, Math.Min(ThumbnailMaxWidth, (int)Math.Round(width * scale)));
        int scaledHeight = Math.Max(1, Math.Min(ThumbnailMaxHeight, (int)Math.Round(height * scale)));
        return (scaledWidth, scaledHeight);
    }

    private static void ValidateDimensions(int width, int height, int actualWidth, int actualHeight)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
        if (width != actualWidth || height != actualHeight)
            throw new ArgumentException(
                $"Declared dimensions {width}x{height} do not match decoded PNG dimensions {actualWidth}x{actualHeight}.");
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
