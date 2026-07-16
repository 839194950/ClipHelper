using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LocalClipboard.Core.Models;

namespace LocalClipboard.Infrastructure.Windows;

public static class ClipboardReader
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(160)
    ];

    public static Task<ClipboardCapture?> ReadAsync(CancellationToken cancellationToken)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Clipboard reads require an STA thread.");
        }

        for (int attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return Task.FromResult(ReadCapture());
            }
            catch (ExternalException) when (attempt < RetryDelays.Length)
            {
                if (cancellationToken.WaitHandle.WaitOne(RetryDelays[attempt]))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            catch (ExternalException)
            {
                return Task.FromResult<ClipboardCapture?>(null);
            }
        }

        return Task.FromResult<ClipboardCapture?>(null);
    }

    private static ClipboardCapture? ReadCapture()
    {
        if (Clipboard.ContainsImage())
        {
            using Image? clipboardImage = Clipboard.GetImage();
            if (clipboardImage is null) return null;

            using var image = new Bitmap(clipboardImage);
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            return new ClipboardCapture(
                ClipboardContentType.Image,
                null,
                stream.ToArray(),
                image.Width,
                image.Height,
                DateTimeOffset.UtcNow);
        }

        if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
        {
            return new ClipboardCapture(
                ClipboardContentType.Text,
                Clipboard.GetText(TextDataFormat.UnicodeText),
                null,
                0,
                0,
                DateTimeOffset.UtcNow);
        }

        return null;
    }
}
