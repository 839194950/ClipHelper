namespace LocalClipboard.Core.Models;

public sealed record HistoryQuery(
    string? Search = null,
    ClipboardContentType? ContentType = null,
    bool FavoritesOnly = false,
    int Limit = 100,
    int Offset = 0);
