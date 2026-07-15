namespace LocalClipboard.Core.Models;

public sealed record RetentionLimits(
    int MaximumEntries,
    TimeSpan MaximumAge,
    long MaximumImageBytes,
    long MaximumSingleImageBytes)
{
    public static RetentionLimits Default { get; } = new(
        500,
        TimeSpan.FromDays(30),
        1_073_741_824,
        20_971_520);
}
