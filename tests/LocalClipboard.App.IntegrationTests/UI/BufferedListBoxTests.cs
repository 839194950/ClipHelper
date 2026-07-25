using LocalClipboard.App.IntegrationTests.Windows;
using LocalClipboard.App.UI;
using LocalClipboard.Core.Models;
using System.Drawing;
using System.Windows.Forms;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class BufferedListBoxTests
{
    [Fact]
    public Task ScrollAnimationUsesMeasuredDeltaAndStopsAtTarget() => StaTest.RunAsync(() =>
    {
        using var timeline = CreateTimeline();
        timeline.RequestWheelScroll(-120);

        timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(24));
        int firstOffset = timeline.ScrollOffset;
        timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(48));
        int secondOffset = timeline.ScrollOffset;
        AdvanceUntilSettled(timeline);

        Assert.InRange(firstOffset, 1, timeline.ItemHeight - 1);
        Assert.True(secondOffset > firstOffset);
        Assert.False(timeline.IsScrollAnimating);
        return Task.CompletedTask;
    });

    [Fact]
    public Task HighResolutionWheelDeltaScalesTheTargetInsteadOfForcingOneNotch() => StaTest.RunAsync(() =>
    {
        using var timeline = CreateTimeline();
        timeline.RequestWheelScroll(-30);

        Assert.InRange(timeline.ScrollTarget, 1, timeline.ItemHeight - 1);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BackgroundFramePumpPostsProgressToTheUiThread() => StaTest.RunAsync(() =>
    {
        using var form = new Form();
        using var timeline = CreateTimeline();
        form.Controls.Add(timeline);
        form.Show();
        timeline.RequestWheelScroll(-120);

        for (int attempt = 0; attempt < 100 && timeline.ScrollOffset == 0; attempt++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        Assert.True(timeline.ScrollOffset > 0);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ScrollbarThumbMapsScrollOffsetToTrack() => StaTest.RunAsync(() =>
    {
        using var timeline = CreateTimeline();
        Rectangle topThumb = timeline.GetScrollbarThumbBounds();

        timeline.RequestScrollTarget(timeline.MaximumScrollOffset);
        AdvanceUntilSettled(timeline);
        Rectangle bottomThumb = timeline.GetScrollbarThumbBounds();

        Assert.True(topThumb.Top < bottomThumb.Top);
        Assert.Equal(6, topThumb.Top);
        Assert.True(bottomThumb.Bottom <= timeline.ClientSize.Height - 6);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DraggingScrollbarMovesDirectlyAndStopsWheelAnimation() => StaTest.RunAsync(() =>
    {
        using var timeline = CreateTimeline();
        timeline.RequestWheelScroll(-120);
        Rectangle thumb = timeline.GetScrollbarThumbBounds();

        Assert.True(timeline.BeginScrollbarInteraction(new Point(thumb.Left, thumb.Top + 4)));
        timeline.UpdateScrollbarDrag(new Point(thumb.Left, timeline.ClientSize.Height - 12));

        Assert.False(timeline.IsScrollAnimating);
        Assert.True(timeline.IsScrollbarDragging);
        Assert.True(timeline.ScrollOffset > timeline.ItemHeight);
        timeline.EndScrollbarInteraction();
        Assert.False(timeline.IsScrollbarDragging);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClickingScrollbarTrackRequestsOneViewportJump() => StaTest.RunAsync(() =>
    {
        using var timeline = CreateTimeline();
        Rectangle thumb = timeline.GetScrollbarThumbBounds();

        timeline.BeginScrollbarInteraction(new Point(thumb.Left, thumb.Bottom + 20));

        Assert.True(timeline.IsScrollAnimating);
        Assert.InRange(timeline.ScrollTarget, timeline.ClientSize.Height - timeline.ItemHeight, timeline.ClientSize.Height);
        return Task.CompletedTask;
    });

    private static BufferedListBox CreateTimeline()
    {
        var timeline = new BufferedListBox { Size = new Size(560, 300), ItemHeight = 92 };
        for (int index = 0; index < 30; index++)
        {
            timeline.Items.Add(new ClipboardEntryView(new ClipboardEntry(
                Guid.NewGuid(), ClipboardContentType.Text, $"item {index}", $"hash-{index}",
                null, null, 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false), null));
        }
        return timeline;
    }

    private static void AdvanceUntilSettled(BufferedListBox timeline)
    {
        for (int frame = 0; frame < 120 && timeline.IsScrollAnimating; frame++)
        {
            timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(16));
        }
    }
}
