namespace LocalClipboard.Core.Models;

public sealed record ClipboardCapture(
    ClipboardContentType ContentType,
    string? Text,
    byte[]? PngBytes,
    int Width,
    int Height,
    DateTimeOffset CapturedAt);
