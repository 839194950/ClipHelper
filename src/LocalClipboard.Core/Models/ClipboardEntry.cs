namespace LocalClipboard.Core.Models;

public sealed record ClipboardEntry(
    Guid Id,
    ClipboardContentType ContentType,
    string? TextContent,
    string ContentHash,
    string? ImagePath,
    string? ThumbnailPath,
    int Width,
    int Height,
    long EncodedSize,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    bool IsFavorite);
