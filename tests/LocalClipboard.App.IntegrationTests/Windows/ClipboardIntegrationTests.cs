using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LocalClipboard.Core.Models;
using LocalClipboard.Infrastructure.Windows;

namespace LocalClipboard.App.IntegrationTests.Windows;

[CollectionDefinition(nameof(ClipboardCollection), DisableParallelization = true)]
public sealed class ClipboardCollection;

[Collection(nameof(ClipboardCollection))]
public sealed class ClipboardIntegrationTests : IDisposable
{
    private readonly string testRoot = Path.Combine(Path.GetTempPath(), "LocalClipboard.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public Task ReadAsync_RetriesAndReadsText() => StaTest.RunAsync(async () =>
    {
        Clipboard.SetText("clipboard text");
        ClipboardCapture? capture = await ClipboardReader.ReadAsync(default);

        Assert.NotNull(capture);
        Assert.Equal(ClipboardContentType.Text, capture.ContentType);
        Assert.Equal("clipboard text", capture.Text);
    });

    [Fact]
    public Task ReadAsync_EncodesBitmapAsPng() => StaTest.RunAsync(async () =>
    {
        using var bitmap = new Bitmap(16, 8);
        Clipboard.SetImage(bitmap);

        ClipboardCapture? capture = await ClipboardReader.ReadAsync(default);

        Assert.NotNull(capture?.PngBytes);
        Assert.Equal(16, capture.Width);
        Assert.Equal(8, capture.Height);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], capture.PngBytes![..4]);
    });

    [Fact]
    public Task Writer_RestoresTextAndSuppressesOneNotification() => StaTest.RunAsync(() =>
    {
        var gate = new FakeSuppressionGate();
        var writer = new ClipboardWriter(gate);

        writer.Write(CreateTextEntry("restored"), imageRoot: string.Empty);

        Assert.Equal("restored", Clipboard.GetText());
        Assert.Equal(1, gate.Suppressions);
        Assert.Equal(0, gate.Cancellations);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Writer_CancelsSuppressionWhenWriteFails() => StaTest.RunAsync(() =>
    {
        var gate = new FakeSuppressionGate();
        var writer = new ClipboardWriter(gate);
        ClipboardEntry missingImage = CreateImageEntry("images/missing.png");

        Assert.ThrowsAny<IOException>(() => writer.Write(missingImage, testRoot));
        Assert.Equal(1, gate.Suppressions);
        Assert.Equal(1, gate.Cancellations);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Monitor_PauseSkipsUpdatesWithoutConsumingSuppression() => StaTest.RunAsync(() =>
    {
        const int wmClipboardUpdate = 0x031D;
        var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new ClipboardMonitorWindow(_ =>
        {
            notification.TrySetResult();
            return Task.CompletedTask;
        });

        monitor.SuppressNextNotification();
        monitor.IsPaused = true;
        SendMessage(monitor.Handle, wmClipboardUpdate, nint.Zero, nint.Zero);
        Thread.Sleep(50);
        Assert.False(notification.Task.IsCompleted);

        monitor.IsPaused = false;
        SendMessage(monitor.Handle, wmClipboardUpdate, nint.Zero, nint.Zero);
        Thread.Sleep(50);
        Assert.False(notification.Task.IsCompleted);

        SendMessage(monitor.Handle, wmClipboardUpdate, nint.Zero, nint.Zero);
        Assert.True(notification.Task.Wait(TimeSpan.FromSeconds(2)));
        return Task.CompletedTask;
    });

    public void Dispose()
    {
        if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
    }

    private static ClipboardEntry CreateTextEntry(string text) => new(
        Guid.NewGuid(), ClipboardContentType.Text, text, "hash", null, null, 0, 0, 0,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

    private static ClipboardEntry CreateImageEntry(string imagePath) => new(
        Guid.NewGuid(), ClipboardContentType.Image, null, "hash", imagePath, "images/missing-thumb.png", 1, 1, 1,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);

    private sealed class FakeSuppressionGate : IClipboardSuppressionGate
    {
        public int Suppressions { get; private set; }
        public int Cancellations { get; private set; }

        public void SuppressNextNotification() => Suppressions++;
        public void CancelSuppression() => Cancellations++;
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}

internal static class StaTest
{
    public static async Task RunAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task;
        thread.Join();
    }
}
