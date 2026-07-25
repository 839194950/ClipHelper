using LocalClipboard.Core.Models;

namespace LocalClipboard.App.UI;

internal sealed class ClipboardEntryView : IDisposable
{
    internal ClipboardEntryView(ClipboardEntry entry, Image? thumbnail)
    {
        Entry = entry;
        Thumbnail = thumbnail;
        DisplayText = entry.ContentType == ClipboardContentType.Image
            ? $"{entry.Width} × {entry.Height} image"
            : BuildDisplayText(entry.TextContent);
        DisplayTime = entry.LastUsedAt.ToLocalTime().ToString("MM-dd HH:mm");
    }

    public ClipboardEntry Entry { get; private set; }
    public Image? Thumbnail { get; }
    public string DisplayText { get; }
    public string DisplayTime { get; }

    public void UpdateFavorite(bool isFavorite) => Entry = Entry with { IsFavorite = isFavorite };

    public void Dispose() => Thumbnail?.Dispose();

    private static string BuildDisplayText(string? content)
    {
        string text = (content ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= 180 ? text : text[..180];
    }
}
