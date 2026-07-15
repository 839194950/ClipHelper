using LocalClipboard.Core.Models;

namespace LocalClipboard.Core.Services;

public static class RetentionPolicy
{
    public static IReadOnlySet<Guid> SelectForDeletion(IReadOnlyList<ClipboardEntry> entries, DateTimeOffset now, RetentionLimits limits)
    {
        var ordinary = entries.Where(entry => !entry.IsFavorite).ToList();
        var selected = new HashSet<Guid>();
        var cutoff = now - limits.MaximumAge;

        foreach (var entry in ordinary.Where(entry => entry.LastUsedAt < cutoff)) selected.Add(entry.Id);

        var remaining = ordinary.Where(entry => !selected.Contains(entry.Id)).OrderByDescending(entry => entry.LastUsedAt).ToList();
        foreach (var entry in remaining.Skip(Math.Max(0, limits.MaximumEntries))) selected.Add(entry.Id);

        var imageBytes = ordinary.Where(entry => entry.ImagePath is not null && !selected.Contains(entry.Id)).Sum(entry => entry.EncodedSize);
        foreach (var entry in ordinary.Where(entry => entry.ImagePath is not null && !selected.Contains(entry.Id)).OrderBy(entry => entry.LastUsedAt))
        {
            if (imageBytes <= limits.MaximumImageBytes) break;
            selected.Add(entry.Id);
            imageBytes -= entry.EncodedSize;
        }

        return selected;
    }
}
