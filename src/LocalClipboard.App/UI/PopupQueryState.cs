using LocalClipboard.Core.Models;

namespace LocalClipboard.App.UI;

internal enum PopupFilter
{
    All,
    Text,
    Images,
    Favorites
}

internal sealed record PopupQueryState
{
    public PopupQueryState(string? search, PopupFilter filter, int offset = 0)
    {
        Search = search;
        Filter = filter;
        Offset = offset;
    }

    public string? Search { get; init; }
    public PopupFilter Filter { get; init; }
    public int Offset { get; init; }

    public HistoryQuery ToHistoryQuery() => new(
        Search: string.IsNullOrWhiteSpace(Search) ? null : Search,
        ContentType: Filter switch
        {
            PopupFilter.Text => ClipboardContentType.Text,
            PopupFilter.Images => ClipboardContentType.Image,
            _ => null
        },
        FavoritesOnly: Filter == PopupFilter.Favorites,
        Limit: 100,
        Offset: Math.Max(0, Offset));
}
