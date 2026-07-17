# Popup List Rendering And Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent long clipboard previews from overlapping adjacent rows and require double-click or Enter, rather than a single click, to restore history content.

**Architecture:** Keep the existing fixed-height owner-drawn ListBox, but increase its row height and centralize the two-line summary rectangle so drawing is clipped safely inside each item. Split mouse behavior into single-click selection/favorite handling and double-click activation, preserving the existing keyboard activation path.

**Tech Stack:** C# 14, .NET 10, WinForms owner drawing, xUnit.

---

### Task 1: Constrain Long Text To Two Lines

**Files:**
- Modify: `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`
- Modify: `src/LocalClipboard.App/UI/PopupForm.cs`

- [ ] **Step 1: Write failing layout tests**

Extend `PopupForm_UsesApprovedWindowBehavior` to locate the timeline ListBox and require a 92-pixel fixed item height:

```csharp
ListBox timeline = Assert.IsType<ListBox>(form.Controls.OfType<ListBox>().Single());
Assert.Equal(92, timeline.ItemHeight);
```

Add a focused summary-boundary test:

```csharp
[Fact]
public void PopupForm_TextSummaryBoundsStayInsideListItem()
{
    var itemBounds = new Rectangle(0, 0, 560, 92);

    RectangleF summaryBounds = PopupForm.GetSummaryBounds(itemBounds, textLeft: 14, starLeft: 518);

    Assert.Equal(31, summaryBounds.Top);
    Assert.Equal(44, summaryBounds.Height);
    Assert.True(summaryBounds.Bottom <= itemBounds.Bottom - 10);
}
```

- [ ] **Step 2: Run the layout tests and verify red**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm_UsesApprovedWindowBehavior|FullyQualifiedName~PopupForm_TextSummaryBoundsStayInsideListItem"
```

Expected: FAIL because the timeline currently uses 78 pixels and `GetSummaryBounds` does not exist.

- [ ] **Step 3: Implement bounded two-line drawing**

Add constants to `PopupForm`:

```csharp
private const int TimelineItemHeight = 92;
private const int SummaryTopOffset = 31;
private const int SummaryHeight = 44;
```

Set `timeline.ItemHeight = TimelineItemHeight` and update the footer text to explain the new activation behavior:

```csharp
private const string FooterText = "↑↓ 选择  双击/Enter 恢复  Delete 删除  Esc 关闭";
```

Add the production layout helper used by both drawing and tests:

```csharp
internal static RectangleF GetSummaryBounds(Rectangle itemBounds, int textLeft, int starLeft) => new(
    textLeft,
    itemBounds.Top + SummaryTopOffset,
    Math.Max(1, starLeft - textLeft - 8),
    SummaryHeight);
```

Replace the current summary rectangle and unrestricted `DrawString` call with a clipped two-line layout:

```csharp
RectangleF summaryBounds = GetSummaryBounds(e.Bounds, textLeft, starBounds.Left);
using var summaryFormat = new StringFormat(StringFormat.GenericTypographic)
{
    Trimming = StringTrimming.EllipsisCharacter,
    FormatFlags = StringFormatFlags.LineLimit
};
var graphicsState = e.Graphics.Save();
try
{
    e.Graphics.SetClip(summaryBounds);
    e.Graphics.DrawString(view.DisplayText, summaryFont, primaryBrush, summaryBounds, summaryFormat);
}
finally
{
    e.Graphics.Restore(graphicsState);
}
```

- [ ] **Step 4: Run popup layout tests and verify green**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm"
```

Expected: all popup tests pass.

### Task 2: Require Double-Click Or Enter To Restore

**Files:**
- Modify: `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`
- Modify: `src/LocalClipboard.App/UI/PopupForm.cs`

- [ ] **Step 1: Write failing mouse interaction tests**

Add a test that injects one text view, raises one mouse-down, verifies no activation, then raises a double-click and verifies activation:

```csharp
[Fact]
public Task PopupForm_SingleClickSelectsAndDoubleClickActivates() => StaTest.RunAsync(() =>
{
    int activationCount = 0;
    using PopupForm form = CreateInteractiveForm(() => activationCount++);
    ListBox timeline = PrepareTimeline(form);
    Rectangle itemBounds = timeline.GetItemRectangle(0);
    var click = new MouseEventArgs(MouseButtons.Left, 1, itemBounds.Left + 24, itemBounds.Top + 45, 0);

    RaiseMouseEvent(timeline, "OnMouseDown", click);

    Assert.Equal(0, activationCount);
    Assert.Equal(0, timeline.SelectedIndex);

    RaiseMouseEvent(timeline, "OnMouseDoubleClick", new MouseEventArgs(
        MouseButtons.Left, 2, click.X, click.Y, 0));

    Assert.Equal(1, activationCount);
    return Task.CompletedTask;
});
```

Add a star-specific test that simulates the first and second mouse-down plus double-click and requires exactly one favorite toggle with no activation:

```csharp
[Fact]
public Task PopupForm_DoubleClickingStarTogglesFavoriteOnceWithoutActivating() => StaTest.RunAsync(() =>
{
    int activationCount = 0;
    int favoriteCount = 0;
    using PopupForm form = CreateInteractiveForm(() => activationCount++, () => favoriteCount++);
    ListBox timeline = PrepareTimeline(form);
    Rectangle itemBounds = timeline.GetItemRectangle(0);
    int starX = itemBounds.Right - 27;
    int starY = itemBounds.Top + 36;

    RaiseMouseEvent(timeline, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, starX, starY, 0));
    RaiseMouseEvent(timeline, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 2, starX, starY, 0));
    RaiseMouseEvent(timeline, "OnMouseDoubleClick", new MouseEventArgs(MouseButtons.Left, 2, starX, starY, 0));

    Assert.Equal(1, favoriteCount);
    Assert.Equal(0, activationCount);
    return Task.CompletedTask;
});
```

Add shared helpers to construct a real `PopupForm`, populate its real ListBox with one `ClipboardEntryView`, and invoke protected mouse lifecycle methods:

```csharp
private static PopupForm CreateInteractiveForm(Action activated, Action? favoriteChanged = null) => new(
    Path.GetTempPath(),
    static (_, _) => Task.FromResult<IReadOnlyList<ClipboardEntry>>([]),
    static (_, _) => Task.CompletedTask,
    (_, _, _) =>
    {
        favoriteChanged?.Invoke();
        return Task.CompletedTask;
    },
    _ =>
    {
        activated();
        return Task.CompletedTask;
    });

private static ListBox PrepareTimeline(PopupForm form)
{
    form.CreateControl();
    ListBox timeline = Assert.IsType<ListBox>(form.Controls.OfType<ListBox>().Single());
    timeline.CreateControl();
    var entry = new ClipboardEntry(
        Guid.NewGuid(), ClipboardContentType.Text, "long text", "hash", null, null, 0, 0, 0,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);
    timeline.Items.Add(new ClipboardEntryView(entry, null));
    return timeline;
}

private static void RaiseMouseEvent(Control control, string methodName, MouseEventArgs args) =>
    typeof(Control)
        .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(control, [args]);
```

- [ ] **Step 2: Run mouse tests and verify red**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm_SingleClickSelectsAndDoubleClickActivates|FullyQualifiedName~PopupForm_DoubleClickingStarTogglesFavoriteOnceWithoutActivating"
```

Expected: FAIL because ordinary mouse-down currently activates immediately and repeated star mouse-down toggles twice.

- [ ] **Step 3: Split single-click and double-click behavior**

Subscribe to `timeline.MouseDoubleClick`. Change `Timeline_MouseDown` so it always selects the row, handles only a first click on the star, and never activates ordinary content:

```csharp
private async void Timeline_MouseDown(object? sender, MouseEventArgs e)
{
    if (e.Button != MouseButtons.Left) return;
    int index = timeline.IndexFromPoint(e.Location);
    if (index == ListBox.NoMatches) return;
    timeline.SelectedIndex = index;
    var view = (ClipboardEntryView)timeline.Items[index];

    if (GetStarBounds(timeline.GetItemRectangle(index)).Contains(e.Location))
    {
        if (e.Clicks > 1) return;
        bool favorite = !view.Entry.IsFavorite;
        if (await TryRunUiActionAsync(() => setFavorite(view.Entry.Id, favorite, CancellationToken.None)))
        {
            view.UpdateFavorite(favorite);
            timeline.Invalidate(timeline.GetItemRectangle(index));
        }
    }
}
```

Add the double-click activation handler:

```csharp
private async void Timeline_MouseDoubleClick(object? sender, MouseEventArgs e)
{
    if (e.Button != MouseButtons.Left) return;
    int index = timeline.IndexFromPoint(e.Location);
    if (index == ListBox.NoMatches) return;
    Rectangle itemBounds = timeline.GetItemRectangle(index);
    if (GetStarBounds(itemBounds).Contains(e.Location)) return;

    timeline.SelectedIndex = index;
    var view = (ClipboardEntryView)timeline.Items[index];
    if (await TryRunUiActionAsync(() => activateEntry(view.Entry))) Hide();
}
```

- [ ] **Step 4: Run all popup tests and verify green**

Run:

```powershell
dotnet test tests/LocalClipboard.App.IntegrationTests/LocalClipboard.App.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PopupForm"
```

Expected: all popup tests pass.

### Task 3: Verify And Re-run Desktop Acceptance

**Files:**
- Verify: `src/LocalClipboard.App/UI/PopupForm.cs`
- Verify: `tests/LocalClipboard.App.IntegrationTests/UI/PopupFormTests.cs`

- [ ] **Step 1: Run complete automated verification**

```powershell
dotnet test LocalClipboard.slnx --no-restore
dotnet build LocalClipboard.slnx --no-restore -warnaserror
git diff --check
```

Expected: 0 failed tests, 0 warnings, 0 errors, and no whitespace errors.

- [ ] **Step 2: Run desktop acceptance**

Verify that long text is limited to two lines without overlapping adjacent rows, single-click only selects, double-click and Enter restore, double-clicking the star does not restore, scrolling remains stable, and taskbar activation still refreshes the list.

- [ ] **Step 3: Commit the completed Task 12 work**

```powershell
git add docs src tests
git commit -m "feat: compose tray clipboard application"
```
