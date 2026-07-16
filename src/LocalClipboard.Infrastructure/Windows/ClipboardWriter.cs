using System.Drawing;
using System.Windows.Forms;
using LocalClipboard.Core.Models;

namespace LocalClipboard.Infrastructure.Windows;

public interface IClipboardSuppressionGate
{
    void SuppressNextNotification();
    void CancelSuppression();
}

public sealed class ClipboardWriter(IClipboardSuppressionGate suppressionGate)
{
    public void Write(ClipboardEntry entry, string imageRoot)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Clipboard writes require an STA thread.");
        }

        suppressionGate.SuppressNextNotification();
        try
        {
            if (entry.ContentType == ClipboardContentType.Text)
            {
                Clipboard.SetText(entry.TextContent ?? string.Empty, TextDataFormat.UnicodeText);
                return;
            }

            string imagePath = ResolveImagePath(imageRoot, entry.ImagePath);
            using Image image = Image.FromFile(imagePath);
            using var clone = new Bitmap(image);
            Clipboard.SetImage(clone);
        }
        catch
        {
            suppressionGate.CancelSuppression();
            throw;
        }
    }

    private static string ResolveImagePath(string imageRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new IOException("The clipboard image path is invalid.");
        }

        string root = Path.GetFullPath(imageRoot);
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        string rootPrefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The clipboard image path escapes the image root.");
        }

        return path;
    }
}
