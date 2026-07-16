using LocalClipboard.Infrastructure.Diagnostics;

namespace LocalClipboard.Infrastructure.Tests.Diagnostics;

public sealed class RollingFileLoggerTests : IDisposable
{
    private readonly string logDirectory = Path.Combine(
        Path.GetTempPath(),
        "LocalClipboard.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WriteAsync_DoesNotPersistClipboardContent()
    {
        var logger = new RollingFileLogger(logDirectory, maximumBytes: 1024);
        await logger.WriteAsync(
            "clipboard_read_failed",
            new InvalidOperationException("secret clipboard text"),
            default);

        string log = await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(logDirectory)));
        Assert.Contains("clipboard_read_failed", log);
        Assert.Contains(nameof(InvalidOperationException), log);
        Assert.DoesNotContain("secret clipboard text", log);
    }

    [Fact]
    public async Task WriteAsync_RotatesBeforeDirectoryExceedsLimit()
    {
        var logger = new RollingFileLogger(logDirectory, maximumBytes: 300);
        for (int index = 0; index < 20; index++)
        {
            await logger.WriteAsync("event_" + index, new InvalidOperationException(), default);
        }

        long total = Directory.GetFiles(logDirectory).Sum(path => new FileInfo(path).Length);
        Assert.True(total <= 600);
    }

    public void Dispose()
    {
        if (Directory.Exists(logDirectory)) Directory.Delete(logDirectory, recursive: true);
    }
}
