namespace LocalClipboard.Core.Models;

public sealed record RetentionLimits(
    int MaximumEntries,
    TimeSpan MaximumAge,
    long MaximumImageBytes,
    long MaximumSingleImageBytes)
{
    public static RetentionLimits Default { get; } = new(
        MaximumEntries: 500,
        MaximumAge: TimeSpan.FromDays(30),
        MaximumImageBytes: 1_073_741_824,
        MaximumSingleImageBytes: 20_971_520);
}
