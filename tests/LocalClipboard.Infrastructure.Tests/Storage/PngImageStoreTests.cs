using System.Drawing;
using System.Drawing.Imaging;
using LocalClipboard.Core.Abstractions;
using LocalClipboard.Infrastructure.Storage;

namespace LocalClipboard.Infrastructure.Tests.Storage;

public sealed class PngImageStoreTests : IDisposable
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), "LocalClipboardTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
    }

    [Fact]
    public void Constructor_CreatesImageAndThumbnailDirectories()
    {
        _ = new PngImageStore(rootPath);

        Assert.True(Directory.Exists(Path.Combine(rootPath, "images")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "thumbnails")));
    }

    [Fact]
    public async Task SaveAsync_WritesOriginalAndBoundedThumbnail()
    {
        byte[] png = CreatePng(400, 200);
        Guid entryId = Guid.NewGuid();
        var store = new PngImageStore(rootPath);

        StoredImage result = await store.SaveAsync(entryId, new string('a', 64), png, 400, 200, CancellationToken.None);

        Assert.Equal($"images/{entryId:N}-aaaaaaaaaaaa.png", result.ImagePath);
        Assert.Equal($"thumbnails/{entryId:N}-aaaaaaaaaaaa.png", result.ThumbnailPath);
        Assert.Equal(400, result.Width);
        Assert.Equal(200, result.Height);
        Assert.Equal(png.LongLength, result.EncodedSize);
        Assert.True(File.Exists(ToAbsolutePath(result.ImagePath)));
        Assert.True(File.Exists(ToAbsolutePath(result.ThumbnailPath)));
        Assert.Equal(png, await File.ReadAllBytesAsync(ToAbsolutePath(result.ImagePath)));
        using Image thumbnail = Image.FromFile(ToAbsolutePath(result.ThumbnailPath));
        Assert.Equal(320, thumbnail.Width);
        Assert.Equal(160, thumbnail.Height);
    }

    [Fact]
    public async Task DeleteAsync_RemovesBothFilesAndIgnoresMissingFiles()
    {
        var store = new PngImageStore(rootPath);
        StoredImage saved = await store.SaveAsync(Guid.NewGuid(), new string('b', 64), CreatePng(), 4, 2, CancellationToken.None);

        await store.DeleteAsync(saved.ImagePath, saved.ThumbnailPath, CancellationToken.None);
        await store.DeleteAsync(saved.ImagePath, saved.ThumbnailPath, CancellationToken.None);

        Assert.False(File.Exists(ToAbsolutePath(saved.ImagePath)));
        Assert.False(File.Exists(ToAbsolutePath(saved.ThumbnailPath)));
    }

    [Fact]
    public async Task DeleteOrphansAsync_DeletesOnlyUnreferencedImageFiles()
    {
        var store = new PngImageStore(rootPath);
        StoredImage keep = await store.SaveAsync(Guid.NewGuid(), new string('c', 64), CreatePng(), 4, 2, CancellationToken.None);
        StoredImage remove = await store.SaveAsync(Guid.NewGuid(), new string('d', 64), CreatePng(), 4, 2, CancellationToken.None);
        IReadOnlySet<string> referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            keep.ImagePath,
            keep.ThumbnailPath,
        };

        await store.DeleteOrphansAsync(referenced, CancellationToken.None);

        Assert.True(File.Exists(ToAbsolutePath(keep.ImagePath)));
        Assert.True(File.Exists(ToAbsolutePath(keep.ThumbnailPath)));
        Assert.False(File.Exists(ToAbsolutePath(remove.ImagePath)));
        Assert.False(File.Exists(ToAbsolutePath(remove.ThumbnailPath)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-")]
    public async Task SaveAsync_RejectsInvalidHash(string hash)
    {
        var store = new PngImageStore(rootPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(Guid.NewGuid(), hash, CreatePng(), 4, 2, CancellationToken.None));

        AssertStorageIsEmpty();
    }

    [Fact]
    public async Task SaveAsync_RejectsInvalidPngWithoutLeavingFiles()
    {
        var store = new PngImageStore(rootPath);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            store.SaveAsync(Guid.NewGuid(), new string('e', 64), [1, 2, 3, 4], 4, 2, CancellationToken.None));

        AssertStorageIsEmpty();
    }

    [Fact]
    public async Task SaveAsync_DoesNotUpscaleThumbnail()
    {
        var store = new PngImageStore(rootPath);

        StoredImage saved = await store.SaveAsync(Guid.NewGuid(), new string('f', 64), CreatePng(), 4, 2, CancellationToken.None);

        using Image thumbnail = Image.FromFile(ToAbsolutePath(saved.ThumbnailPath));
        Assert.Equal(4, thumbnail.Width);
        Assert.Equal(2, thumbnail.Height);
    }

    [Fact]
    public async Task SaveAsync_PreservesAspectRatioWithinThumbnailBounds()
    {
        var store = new PngImageStore(rootPath);

        StoredImage saved = await store.SaveAsync(Guid.NewGuid(), new string('0', 64), CreatePng(400, 100), 400, 100, CancellationToken.None);

        using Image thumbnail = Image.FromFile(ToAbsolutePath(saved.ThumbnailPath));
        Assert.Equal(320, thumbnail.Width);
        Assert.Equal(80, thumbnail.Height);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("images/../outside.png")]
    public async Task DeleteAsync_RejectsParentTraversal(string relativePath)
    {
        var store = new PngImageStore(rootPath);

        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(relativePath, null, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_RejectsRootedPathAndDoesNotDeleteIt()
    {
        Directory.CreateDirectory(rootPath);
        string outsidePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(outsidePath, [1, 2, 3]);
        var store = new PngImageStore(rootPath);

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(outsidePath, null, CancellationToken.None));
            Assert.True(File.Exists(outsidePath));
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Fact]
    public async Task DeleteAsync_ValidatesBothPathsBeforeDeletingEither()
    {
        var store = new PngImageStore(rootPath);
        StoredImage saved = await store.SaveAsync(Guid.NewGuid(), new string('3', 64), CreatePng(), 4, 2, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.DeleteAsync(saved.ImagePath, "../outside.png", CancellationToken.None));

        Assert.True(File.Exists(ToAbsolutePath(saved.ImagePath)));
        Assert.True(File.Exists(ToAbsolutePath(saved.ThumbnailPath)));
    }

    [Fact]
    public async Task SaveAsync_FailureAfterOriginalMoveRemovesNewFilesAndTemps()
    {
        Guid entryId = Guid.NewGuid();
        string hash = new('1', 64);
        string baseName = $"{entryId:N}-{hash[..12]}";
        Directory.CreateDirectory(Path.Combine(rootPath, "thumbnails", $"{baseName}.png"));
        var store = new PngImageStore(rootPath);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            store.SaveAsync(entryId, hash, CreatePng(), 4, 2, CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(rootPath, "images", $"{baseName}.png")));
        Assert.Empty(Directory.Exists(Path.Combine(rootPath, "images"))
            ? Directory.EnumerateFiles(Path.Combine(rootPath, "images"), "*.tmp")
            : []);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(rootPath, "thumbnails"), "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_UsesEntryIdToAvoidSharingFilesForDuplicateHashes()
    {
        var store = new PngImageStore(rootPath);
        string hash = new('2', 64);

        StoredImage first = await store.SaveAsync(Guid.NewGuid(), hash, CreatePng(), 4, 2, CancellationToken.None);
        StoredImage second = await store.SaveAsync(Guid.NewGuid(), hash, CreatePng(), 4, 2, CancellationToken.None);

        Assert.NotEqual(first.ImagePath, second.ImagePath);
        Assert.NotEqual(first.ThumbnailPath, second.ThumbnailPath);
    }

    [Fact]
    public async Task DeleteOrphansAsync_IgnoresNestedAndNonPngFiles()
    {
        string nestedPng = Path.Combine(rootPath, "images", "nested", "nested.png");
        string nonPng = Path.Combine(rootPath, "thumbnails", "note.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedPng)!);
        Directory.CreateDirectory(Path.GetDirectoryName(nonPng)!);
        await File.WriteAllBytesAsync(nestedPng, [1]);
        await File.WriteAllBytesAsync(nonPng, [2]);
        var store = new PngImageStore(rootPath);

        await store.DeleteOrphansAsync(new HashSet<string>(), CancellationToken.None);

        Assert.True(File.Exists(nestedPng));
        Assert.True(File.Exists(nonPng));
    }

    [Fact]
    public async Task DeleteOrphansAsync_ObservesPreCanceledTokenWithoutFiles()
    {
        var store = new PngImageStore(rootPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            store.DeleteOrphansAsync(new HashSet<string>(), cancellation.Token));
    }

    private string ToAbsolutePath(string relativePath) =>
        Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private void AssertStorageIsEmpty()
    {
        Assert.Empty(Directory.Exists(rootPath)
            ? Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            : []);
    }

    private static byte[] CreatePng(int width = 4, int height = 2)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
