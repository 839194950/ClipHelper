using LocalClipboard.Core.Models;

namespace LocalClipboard.App.UI;

internal sealed class ClipboardEntryView(ClipboardEntry entry, Image? thumbnail) : IDisposable
{
    public ClipboardEntry Entry { get; private set; } = entry;
    public Image? Thumbnail { get; } = thumbnail;

    public string DisplayText
    {
        get
        {
            if (Entry.ContentType == ClipboardContentType.Image)
                return $"{Entry.Width} × {Entry.Height} image";

            string text = (Entry.TextContent ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            return text.Length <= 180 ? text : text[..180];
        }
    }

    public void UpdateFavorite(bool isFavorite) => Entry = Entry with { IsFavorite = isFavorite };

    public void Dispose() => Thumbnail?.Dispose();
}
