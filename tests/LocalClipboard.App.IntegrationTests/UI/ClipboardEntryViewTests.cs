using LocalClipboard.App.UI;
using LocalClipboard.Core.Models;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class ClipboardEntryViewTests
{
    [Fact]
    public void DisplayValuesAreCalculatedOnceAndRemainStableAfterFavoriteChange()
    {
        var entry = new ClipboardEntry(
            Guid.NewGuid(), ClipboardContentType.Text, "first\r\nsecond", "hash",
            null, null, 0, 0, 0,
            new DateTimeOffset(2026, 7, 22, 1, 2, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 22, 3, 4, 0, TimeSpan.Zero), false);
        using var view = new ClipboardEntryView(entry, null);

        string displayText = view.DisplayText;
        string displayTime = view.DisplayTime;
        view.UpdateFavorite(true);

        Assert.Same(displayText, view.DisplayText);
        Assert.Same(displayTime, view.DisplayTime);
        Assert.Equal("first  second", view.DisplayText);
        Assert.Equal(entry.LastUsedAt.ToLocalTime().ToString("MM-dd HH:mm"), view.DisplayTime);
    }
}
