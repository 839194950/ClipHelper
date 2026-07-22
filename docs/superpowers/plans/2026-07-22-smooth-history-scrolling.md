# High-Frame-Rate History Scrolling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make clipboard-history scrolling visually smoother while adding a clickable and draggable custom scrollbar without changing existing clipboard behavior.

**Architecture:** Keep `BufferedListBox` as the single owner-drawn timeline control. Move stable display data and thumbnail sizing out of the paint loop, drive wheel animation with measured elapsed time, and isolate scrollbar geometry and pointer interaction inside the timeline control.

**Tech Stack:** C# 14, .NET 10 Windows Forms, System.Drawing, xUnit, existing SQLite-backed history services.

---

## File Structure

- Modify `src/LocalClipboard.App/UI/ClipboardEntryView.cs`: cache display text and local display time once per view.
- Create `src/LocalClipboard.App/UI/ThumbnailScaler.cs`: create a centered, aspect-ratio-preserving list thumbnail at a fixed display size.
- Modify `src/LocalClipboard.App/UI/PopupForm.cs`: load pre-scaled thumbnails and preserve paging callbacks for wheel, drag, and track jumps.
- Modify `src/LocalClipboard.App/UI/PopupListRenderer.cs`: use cached strings, cached images, and `TextRenderer` in the hot paint path.
- Modify `src/LocalClipboard.App/UI/BufferedListBox.cs`: measure real frame deltas, avoid redundant invalidation, calculate scrollbar geometry, and implement drag/track input.
- Create `tests/LocalClipboard.App.IntegrationTests/UI/ClipboardEntryViewTests.cs`: verify stable display caches.
- Create `tests/LocalClipboard.App.IntegrationTests/UI/ThumbnailScalerTests.cs`: verify thumbnail sizing and centering.
- Create `tests/LocalClipboard.App.IntegrationTests/UI/BufferedListBoxTests.cs`: verify frame progression, scrollbar geometry, drag mapping, and track clicks.
- Modify `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`: preserve existing popup interaction and pagination integration coverage.

### Task 1: Cache Stable Display Text

**Files:**
- Modify: `src/LocalClipboard.App/UI/ClipboardEntryView.cs`
- Create: `tests/LocalClipboard.App.IntegrationTests/UI/ClipboardEntryViewTests.cs`

- [ ] **Step 1: Write failing display-cache tests**

Create `ClipboardEntryViewTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the cache test and verify RED**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ClipboardEntryViewTests
```

Expected: compilation fails because `DisplayTime` does not exist, or the reference-stability assertions fail because `DisplayText` is recomputed.

- [ ] **Step 3: Cache both display values in the constructor**

Replace the computed property in `ClipboardEntryView.cs` with constructor initialization:

```csharp
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
```

- [ ] **Step 4: Run the cache test and verify GREEN**

Run the command from Step 2.

Expected: 1 test passes.

- [ ] **Step 5: Commit the display cache**

```powershell
git add src/LocalClipboard.App/UI/ClipboardEntryView.cs tests/LocalClipboard.App.IntegrationTests/UI/ClipboardEntryViewTests.cs
git commit -m "perf: cache clipboard entry display text"
```

### Task 2: Pre-Scale List Thumbnails

**Files:**
- Create: `src/LocalClipboard.App/UI/ThumbnailScaler.cs`
- Modify: `src/LocalClipboard.App/UI/PopupForm.cs`
- Create: `tests/LocalClipboard.App.IntegrationTests/UI/ThumbnailScalerTests.cs`

- [ ] **Step 1: Write failing thumbnail sizing tests**

Create `ThumbnailScalerTests.cs`:

```csharp
using LocalClipboard.App.UI;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class ThumbnailScalerTests
{
    [Fact]
    public void CreateListThumbnailFitsWideImageInsideDisplayCanvas()
    {
        using var source = new Bitmap(320, 100);
        using (Graphics graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.CornflowerBlue);
        }
        using Bitmap thumbnail = ThumbnailScaler.CreateListThumbnail(source, new Size(58, 58));

        Assert.Equal(new Size(58, 58), thumbnail.Size);
        Assert.Equal(Color.Transparent.ToArgb(), thumbnail.GetPixel(0, 0).ToArgb());
        Assert.NotEqual(Color.Transparent.ToArgb(), thumbnail.GetPixel(29, 29).ToArgb());
    }
}
```


- [ ] **Step 2: Run the thumbnail test and verify RED**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ThumbnailScalerTests
```

Expected: compilation fails because `ThumbnailScaler` does not exist.

- [ ] **Step 3: Implement aspect-ratio-preserving scaling**

Create `ThumbnailScaler.cs`:

```csharp
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LocalClipboard.App.UI;

internal static class ThumbnailScaler
{
    internal static Bitmap CreateListThumbnail(Image source, Size canvasSize)
    {
        if (canvasSize.Width <= 0 || canvasSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(canvasSize));

        double scale = Math.Min(
            canvasSize.Width / (double)source.Width,
            canvasSize.Height / (double)source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        int left = (canvasSize.Width - width) / 2;
        int top = (canvasSize.Height - height) / 2;

        var result = new Bitmap(canvasSize.Width, canvasSize.Height, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(source, new Rectangle(left, top, width, height));
        return result;
    }
}
```

- [ ] **Step 4: Use the scaler once during thumbnail loading**

In `PopupForm.LoadThumbnail`, replace `return new Bitmap(source);` with:

```csharp
return ThumbnailScaler.CreateListThumbnail(source, new Size(58, 58));
```

- [ ] **Step 5: Run thumbnail and popup tests**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ThumbnailScalerTests|FullyQualifiedName~PopupForm"
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit thumbnail caching**

```powershell
git add src/LocalClipboard.App/UI/ThumbnailScaler.cs src/LocalClipboard.App/UI/PopupForm.cs tests/LocalClipboard.App.IntegrationTests/UI/ThumbnailScalerTests.cs
git commit -m "perf: pre-scale popup thumbnails"
```

### Task 3: Use Measured Frame Time And Skip Redundant Frames

**Files:**
- Modify: `src/LocalClipboard.App/UI/BufferedListBox.cs`
- Create: `tests/LocalClipboard.App.IntegrationTests/UI/BufferedListBoxTests.cs`

- [ ] **Step 1: Write failing measured-scroll tests**

Create `BufferedListBoxTests.cs` with an STA test:

```csharp
using LocalClipboard.App.UI;
using LocalClipboard.Core.Models;
using LocalClipboard.App.IntegrationTests.Windows;

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
        timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(24));

        Assert.InRange(firstOffset, 1, timeline.ItemHeight - 1);
        Assert.True(secondOffset > firstOffset);
        Assert.False(timeline.IsScrollAnimating);
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
}
```

- [ ] **Step 2: Run the measured-scroll test and verify RED**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~BufferedListBoxTests.ScrollAnimationUsesMeasuredDeltaAndStopsAtTarget
```

Expected: the test fails against the old 120ms fixed-step behavior or does not stop at the 96ms target.

- [ ] **Step 3: Measure timer deltas with Stopwatch timestamps**

In `BufferedListBox.cs`:

```csharp
private const int ScrollDurationMilliseconds = 96;
private long lastFrameTimestamp;

private void ScrollTimer_Tick(object? sender, EventArgs e)
{
    long now = Stopwatch.GetTimestamp();
    TimeSpan delta = lastFrameTimestamp == 0
        ? TimeSpan.FromMilliseconds(scrollTimer.Interval)
        : Stopwatch.GetElapsedTime(lastFrameTimestamp, now);
    lastFrameTimestamp = now;
    AdvanceScrollAnimation(delta);
}

private void StartScrollAnimation()
{
    scrollAnimation = new PopupAnimation(TimeSpan.FromMilliseconds(ScrollDurationMilliseconds));
    lastFrameTimestamp = Stopwatch.GetTimestamp();
    scrollTimer.Start();
}

private void StopScrollAnimation()
{
    scrollTimer.Stop();
    scrollAnimation = null;
    lastFrameTimestamp = 0;
}
```

Wire `scrollTimer.Tick` to `ScrollTimer_Tick`, and call `StartScrollAnimation` from `RequestWheelScroll`.

- [ ] **Step 4: Skip invalidation when offset does not change**

Change `SetScrollOffset` to return whether the position changed:

```csharp
private bool SetScrollOffset(int value)
{
    int normalized = Math.Clamp(value, 0, MaximumScrollOffset);
    if (normalized == scrollOffset) return false;
    scrollOffset = normalized;
    Invalidate();
    ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
    return true;
}
```

Add:

```csharp
internal event EventHandler? ScrollPositionChanged;
```

Only update frame bookkeeping when `SetScrollOffset(next)` returns true. Replace the private maximum property and shared target setup with:

```csharp
internal int MaximumScrollOffset => Math.Max(0, (Items.Count * ItemHeight) - ClientSize.Height);

internal void RequestScrollTarget(int target)
{
    scrollStart = scrollOffset;
    scrollTarget = Math.Clamp(target, 0, MaximumScrollOffset);
    if (scrollTarget == scrollOffset)
    {
        StopScrollAnimation();
        return;
    }
    StartScrollAnimation();
}
```

Change `RequestWheelScroll` to calculate the accumulated wheel target and pass it to `RequestScrollTarget`; scrollbar-track input must call the same method.

- [ ] **Step 5: Run measured-scroll and existing popup tests**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~BufferedListBoxTests|FullyQualifiedName~PopupForm_TimelineScrollsByPixels"
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit frame pacing**

```powershell
git add src/LocalClipboard.App/UI/BufferedListBox.cs tests/LocalClipboard.App.IntegrationTests/UI/BufferedListBoxTests.cs
git commit -m "perf: pace popup scrolling with real time"
```

### Task 4: Add Draggable And Clickable Scrollbar

**Files:**
- Modify: `src/LocalClipboard.App/UI/BufferedListBox.cs`
- Modify: `tests/LocalClipboard.App.IntegrationTests/UI/BufferedListBoxTests.cs`

- [ ] **Step 1: Write failing scrollbar geometry tests**

Add to `BufferedListBoxTests.cs`:

```csharp
[Fact]
public Task ScrollbarThumbMapsScrollOffsetToTrack() => StaTest.RunAsync(() =>
{
    using var timeline = CreateTimeline();
    Rectangle topThumb = timeline.GetScrollbarThumbBounds();

    timeline.RequestScrollTarget(timeline.MaximumScrollOffset);
    timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(96));
    Rectangle bottomThumb = timeline.GetScrollbarThumbBounds();

    Assert.True(topThumb.Top < bottomThumb.Top);
    Assert.Equal(6, topThumb.Top);
    Assert.True(bottomThumb.Bottom <= timeline.ClientSize.Height - 6);
    return Task.CompletedTask;
});
```

Change the existing maximum-offset property to `internal int MaximumScrollOffset` because scrollbar rendering, drag mapping, paging integration, and tests all require the same authoritative range.

- [ ] **Step 2: Run the geometry test and verify RED**

Run the single test.

Expected: compilation fails because scrollbar geometry and direct scrollbar mapping APIs do not exist.

- [ ] **Step 3: Implement reusable scrollbar geometry**

Add constants and geometry methods:

```csharp
private const int ScrollbarMargin = 6;
private const int ScrollbarNormalWidth = 4;
private const int ScrollbarActiveWidth = 7;
private const int ScrollbarHitWidth = 14;

internal Rectangle GetScrollbarThumbBounds()
{
    if (MaximumScrollOffset == 0) return Rectangle.Empty;
    int trackHeight = Math.Max(1, ClientSize.Height - (ScrollbarMargin * 2));
    int contentHeight = Items.Count * ItemHeight;
    int thumbHeight = Math.Max(32, (int)Math.Round(trackHeight * (ClientSize.Height / (double)contentHeight)));
    int travel = Math.Max(1, trackHeight - thumbHeight);
    int thumbTop = ScrollbarMargin + (int)Math.Round(travel * (scrollOffset / (double)MaximumScrollOffset));
    int width = scrollbarHovered || scrollbarDragging ? ScrollbarActiveWidth : ScrollbarNormalWidth;
    return new Rectangle(ClientSize.Width - width - 3, thumbTop, width, thumbHeight);
}

private Rectangle GetScrollbarHitBounds()
{
    Rectangle thumb = GetScrollbarThumbBounds();
    return thumb == Rectangle.Empty
        ? Rectangle.Empty
        : new Rectangle(ClientSize.Width - ScrollbarHitWidth, thumb.Top, ScrollbarHitWidth, thumb.Height);
}
```

Use `GetScrollbarThumbBounds` from `DrawScrollbar` so painting and hit testing share one geometry source.

- [ ] **Step 4: Write failing drag and track-click tests**

Add:

```csharp
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
```

- [ ] **Step 5: Implement pointer state and capture**

Add fields:

```csharp
private bool scrollbarHovered;
private bool scrollbarDragging;
private int scrollbarDragOffset;
internal bool IsScrollbarDragging => scrollbarDragging;
internal int ScrollTarget => scrollTarget;
```

Add helpers used by both mouse overrides and tests:

```csharp
internal bool BeginScrollbarInteraction(Point location)
{
    Rectangle thumb = GetScrollbarThumbBounds();
    if (thumb == Rectangle.Empty || location.X < ClientSize.Width - ScrollbarHitWidth) return false;

    StopScrollAnimation();
    if (GetScrollbarHitBounds().Contains(location))
    {
        scrollbarDragging = true;
        scrollbarDragOffset = location.Y - thumb.Top;
        Capture = true;
    }
    else
    {
        int direction = location.Y < thumb.Top ? -1 : 1;
        RequestScrollTarget(scrollOffset + (direction * ClientSize.Height));
    }
    Invalidate();
    return true;
}

internal void UpdateScrollbarDrag(Point location)
{
    if (!scrollbarDragging) return;
    Rectangle thumb = GetScrollbarThumbBounds();
    int trackTop = ScrollbarMargin;
    int travel = Math.Max(1, ClientSize.Height - (ScrollbarMargin * 2) - thumb.Height);
    int thumbTop = Math.Clamp(location.Y - scrollbarDragOffset, trackTop, trackTop + travel);
    double ratio = (thumbTop - trackTop) / (double)travel;
    SetScrollOffset((int)Math.Round(MaximumScrollOffset * ratio));
}

internal void EndScrollbarInteraction()
{
    scrollbarDragging = false;
    Capture = false;
    Invalidate();
}
```

Override `OnMouseDown`, `OnMouseMove`, `OnMouseUp`, and `OnMouseCaptureChanged` to call these helpers before normal item interaction. When a scrollbar interaction consumes the mouse-down, do not let `PopupForm.Timeline_MouseDown` select or activate a history item.

- [ ] **Step 6: Run all scrollbar tests**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~BufferedListBoxTests
```

Expected: geometry, drag, track-click, and frame-pacing tests pass.

- [ ] **Step 7: Commit scrollbar interaction**

```powershell
git add src/LocalClipboard.App/UI/BufferedListBox.cs tests/LocalClipboard.App.IntegrationTests/UI/BufferedListBoxTests.cs
git commit -m "feat: add draggable popup scrollbar"
```

### Task 5: Optimize Text Rendering And Preserve Paging

**Files:**
- Modify: `src/LocalClipboard.App/UI/PopupListRenderer.cs`
- Modify: `src/LocalClipboard.App/UI/PopupForm.cs`
- Modify: `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Write a failing paging callback test**

Add this test to `PopupFormTests.cs`:

```csharp
[Fact]
public Task PopupForm_DirectScrollNearBottomLoadsNextPageOnce() => StaTest.RunAsync(async () =>
{
    var requestedOffsets = new List<int>();
    var appendObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    IReadOnlyList<ClipboardEntry> firstPage = Enumerable.Range(0, 100)
        .Select(index => new ClipboardEntry(
            Guid.NewGuid(), ClipboardContentType.Text, $"item {index}", $"hash-{index}",
            null, null, 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false))
        .ToArray();
    using var form = new PopupForm(
        Path.GetTempPath(),
        (query, _) =>
        {
            requestedOffsets.Add(query.Offset);
            if (query.Offset == 100) appendObserved.TrySetResult();
            return Task.FromResult(query.Offset == 0 ? firstPage : (IReadOnlyList<ClipboardEntry>)[]);
        },
        static (_, _) => Task.CompletedTask,
        static (_, _, _) => Task.CompletedTask,
        static _ => Task.CompletedTask);

    form.Show();
    BufferedListBox timeline = Assert.IsType<BufferedListBox>(Assert.Single(
        form.Controls.Find("Timeline", searchAllChildren: true)));
    await PumpUntilAsync(() => timeline.Items.Count == 100);

    timeline.RequestScrollTarget(timeline.MaximumScrollOffset);
    timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(96));
    await appendObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Application.DoEvents();

    Assert.Equal([0, 100], requestedOffsets);
});

private static async Task PumpUntilAsync(Func<bool> condition)
{
    for (int attempt = 0; attempt < 100 && !condition(); attempt++)
    {
        Application.DoEvents();
        await Task.Delay(10);
    }
    Assert.True(condition());
}
```

- [ ] **Step 2: Run the paging test and verify RED**

Expected: the append query is not triggered by direct scrollbar dragging because `PopupForm` only listens to mouse wheel and selection changes.

- [ ] **Step 3: Draw cached strings with TextRenderer**

In `PopupListRenderer.Draw`, replace `Graphics.DrawString` calls with:

```csharp
TextRenderer.DrawText(
    graphics, view.DisplayTime, baseFont,
    new Rectangle(textLeft, card.Top + 8, starBounds.Left - textLeft - 8, 20),
    palette.SecondaryText,
    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

TextRenderer.DrawText(
    graphics, view.DisplayText, summaryFont, Rectangle.Round(summary), palette.PrimaryText,
    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);

TextRenderer.DrawText(
    graphics, view.Entry.IsFavorite ? "★" : "☆", summaryFont, starBounds, palette.Accent,
    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
```

Remove the cached `StringFormat`, its clipping state, and its disposal.

- [ ] **Step 4: Trigger pagination for all scroll sources**

In `PopupForm`, subscribe once:

```csharp
timeline.ScrollPositionChanged += Timeline_ScrollPositionChanged;
```

Add:

```csharp
private void Timeline_ScrollPositionChanged(object? sender, EventArgs e) => TryLoadNextPage();
```

Keep `Timeline_MouseWheel` responsible only for clearing stale hover state. Remove its direct `TryLoadNextPage` call to avoid duplicate queries.

- [ ] **Step 5: Run popup and renderer regression tests**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm|FullyQualifiedName~BufferedListBoxTests|FullyQualifiedName~ClipboardEntryViewTests|FullyQualifiedName~ThumbnailScalerTests"
```

Expected: all selected tests pass, including single click, double click, star protection, keyboard activation, search, filters, and append pagination.

- [ ] **Step 6: Commit rendering and paging integration**

```powershell
git add src/LocalClipboard.App/UI/PopupListRenderer.cs src/LocalClipboard.App/UI/PopupForm.cs tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs
git commit -m "perf: streamline popup list rendering"
```

### Task 6: Full Verification And Desktop Acceptance

**Files:**
- Verify: `src/LocalClipboard.App/UI/BufferedListBox.cs`
- Verify: `src/LocalClipboard.App/UI/ClipboardEntryView.cs`
- Verify: `src/LocalClipboard.App/UI/ThumbnailScaler.cs`
- Verify: `src/LocalClipboard.App/UI/PopupListRenderer.cs`
- Verify: `src/LocalClipboard.App/UI/PopupForm.cs`

- [ ] **Step 1: Run the complete automated suite**

```powershell
dotnet test LocalClipboard.slnx --no-restore
dotnet build LocalClipboard.slnx --no-restore -warnaserror
dotnet list LocalClipboard.slnx package --vulnerable --include-transitive
git diff --check
```

Expected: all tests pass, build reports 0 warnings and 0 errors, no vulnerable packages are reported, and the diff check exits successfully.

- [ ] **Step 2: Relaunch the current worktree build**

```powershell
Get-Process LocalClipboard.App -ErrorAction SilentlyContinue | Stop-Process -Force
$exe = (Resolve-Path 'src/LocalClipboard.App/bin/Debug/net10.0-windows/LocalClipboard.App.exe').Path
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
```

Expected: `LocalClipboard.App` is running and responding.

- [ ] **Step 3: Perform desktop acceptance**

Verify with at least 100 mixed text/image entries:

1. Rapid wheel scrolling has more even visual frame spacing than the current version.
2. Reversing wheel direction does not pause or queue stale animation.
3. Hovering the scrollbar widens it without flicker.
4. Dragging the thumb tracks the pointer immediately and reaches exact top and bottom positions.
5. Clicking above or below the thumb moves approximately one viewport.
6. Dragging near the loaded bottom triggers only one append query.
7. Stopping input stops the animation timer and CPU activity returns to idle.
8. Existing selection, double-click, Enter, Delete, favorite, search, filter, Esc, close, drag-window, and taskbar behaviors remain intact.

- [ ] **Step 4: Commit final integration if desktop acceptance passes**

```powershell
git add src tests docs/superpowers/plans/2026-07-22-smooth-history-scrolling.md
git diff --cached --check
git commit -m "perf: improve popup scrolling responsiveness"
```

Do not include unrelated files, and do not squash the previously committed design document.
