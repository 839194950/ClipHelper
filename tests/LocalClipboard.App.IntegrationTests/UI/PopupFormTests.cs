using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using LocalClipboard.App.UI;
using LocalClipboard.Core.Models;
using LocalClipboard.App.IntegrationTests.Windows;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class PopupFormTests
{
    [Fact]
    public void QueryState_BuildsFavoritesImageQuery()
    {
        var state = new PopupQueryState("screen", PopupFilter.Favorites, offset: 100);

        HistoryQuery query = state.ToHistoryQuery();

        Assert.Equal("screen", query.Search);
        Assert.True(query.FavoritesOnly);
        Assert.Null(query.ContentType);
        Assert.Equal(100, query.Offset);
        Assert.Equal(100, query.Limit);
    }

    [Fact]
    public Task PopupForm_UsesApprovedWindowBehavior() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();

        Assert.Equal(FormBorderStyle.None, form.FormBorderStyle);
        Assert.True(form.ShowInTaskbar);
        Assert.NotNull(form.Icon);
        Assert.True(form.Icon.Width >= 16);
        Assert.Equal(new Size(600, 1200), form.ClientSize);
        Assert.Equal(new Padding(1), form.Padding);
        Assert.True(form.KeyPreview);
        BufferedListBox timeline = Assert.IsType<BufferedListBox>(form.Controls.OfType<BufferedListBox>().Single());
        Assert.Equal(92, timeline.ItemHeight);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_UsesRefinedVisualHierarchy() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();

        Panel titleBar = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("TitleBar", searchAllChildren: true)));
        Panel searchPanel = Assert.IsType<Panel>(Assert.Single(form.Controls.Find("SearchPanel", searchAllChildren: true)));
        FlowLayoutPanel filterPanel = Assert.IsType<FlowLayoutPanel>(Assert.Single(form.Controls.Find("FilterPanel", searchAllChildren: true)));
        TextBox searchBox = Assert.IsType<TextBox>(Assert.Single(form.Controls.Find("SearchBox", searchAllChildren: true)));

        Assert.Equal(44, titleBar.Height);
        Assert.Equal(58, searchPanel.Height);
        Assert.Equal(42, filterPanel.Height);
        Assert.Equal(BorderStyle.FixedSingle, searchBox.BorderStyle);
        Assert.True(searchBox.TabStop);
        Assert.All(filterPanel.Controls.OfType<Button>(), button =>
        {
            Assert.Equal(FlatStyle.Flat, button.FlatStyle);
            Assert.Equal(0, button.FlatAppearance.BorderSize);
        });
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_UsesPclInspiredVisualHierarchy() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();

        Panel accent = Assert.IsType<Panel>(Assert.Single(
            form.Controls.Find("TitleAccent", searchAllChildren: true)));
        Panel searchHost = Assert.IsType<Panel>(Assert.Single(
            form.Controls.Find("SearchHost", searchAllChildren: true)));
        BufferedListBox timeline = Assert.IsType<BufferedListBox>(Assert.Single(
            form.Controls.Find("Timeline", searchAllChildren: true)));

        Assert.Equal(DockStyle.Left, accent.Dock);
        Assert.True(accent.Width >= 4);
        Assert.True(searchHost.Padding.All >= 1);
        Assert.Equal(DrawMode.OwnerDrawFixed, timeline.DrawMode);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_TimelineUsesBufferedRendering() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();

        BufferedListBox timeline = Assert.IsType<BufferedListBox>(Assert.Single(
            form.Controls.Find("Timeline", searchAllChildren: true)));

        Assert.True(timeline.UsesOptimizedRendering);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_TimelineScrollsByPixelsDuringWheelAnimation() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();
        BufferedListBox timeline = Assert.IsType<BufferedListBox>(Assert.Single(
            form.Controls.Find("Timeline", searchAllChildren: true)));
        timeline.Size = new Size(560, 300);
        for (int index = 0; index < 20; index++)
        {
            timeline.Items.Add(new ClipboardEntryView(new ClipboardEntry(
                Guid.NewGuid(), ClipboardContentType.Text, $"item {index}", $"hash-{index}",
                null, null, 0, 0, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false), null));
        }

        timeline.RequestWheelScroll(-120);
        timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(48));

        Assert.InRange(timeline.ScrollOffset, 1, timeline.ItemHeight - 1);
        Assert.True(timeline.IsScrollAnimating);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_DirectScrollNearBottomLoadsNextPageOnce() => StaTest.RunAsync(() =>
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
        PumpUntil(() => timeline.Items.Count == 100);

        timeline.RequestScrollTarget(timeline.MaximumScrollOffset);
        for (int frame = 0; frame < 120 && timeline.IsScrollAnimating; frame++)
        {
            timeline.AdvanceScrollAnimation(TimeSpan.FromMilliseconds(16));
        }
        PumpUntil(() => appendObserved.Task.IsCompleted);
        Assert.True(appendObserved.Task.IsCompleted);
        Application.DoEvents();

        Assert.Equal([0, 100], requestedOffsets);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_SettingsButtonInvokesSettingsAction() => StaTest.RunAsync(() =>
    {
        int invocationCount = 0;
        using PopupForm form = PopupForm.CreateForTest(() => invocationCount++);
        form.Show();
        Application.DoEvents();

        Button settingsButton = Assert.IsType<Button>(Assert.Single(
            form.Controls.Find("SettingsButton", searchAllChildren: true)));
        settingsButton.PerformClick();

        Assert.Equal(1, invocationCount);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_SearchAndFiltersUseSegmentedControls() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();
        form.Show();

        Button clearButton = Assert.IsType<Button>(Assert.Single(
            form.Controls.Find("ClearSearchButton", searchAllChildren: true)));
        Button allFilter = Assert.IsType<Button>(Assert.Single(
            form.Controls.Find("FilterButton_All", searchAllChildren: true)));
        TextBox searchBox = Assert.IsType<TextBox>(Assert.Single(
            form.Controls.Find("SearchBox", searchAllChildren: true)));

        Assert.False(clearButton.Visible);
        Assert.Equal(FlatStyle.Flat, allFilter.FlatStyle);
        Assert.Equal(0, allFilter.FlatAppearance.BorderSize);

        searchBox.Text = "query";
        Assert.True(clearButton.Visible);
        clearButton.PerformClick();
        Assert.Equal(string.Empty, searchBox.Text);
        return Task.CompletedTask;
    });

    [Fact]
    public void PopupForm_TextSummaryBoundsStayInsideListItem()
    {
        var itemBounds = new Rectangle(0, 0, 560, 92);

        RectangleF summaryBounds = PopupForm.GetSummaryBounds(itemBounds, textLeft: 14, starLeft: 518);

        Assert.Equal(31, summaryBounds.Top);
        Assert.Equal(44, summaryBounds.Height);
        Assert.True(summaryBounds.Bottom <= itemBounds.Bottom - 10);
    }

    [Fact]
    public Task PopupForm_DeactivationStaysVisibleAndCloseButtonHides() => StaTest.RunAsync(() =>
    {
        using PopupForm form = PopupForm.CreateForTest();
        form.Show();
        Application.DoEvents();

        typeof(Form)
            .GetMethod("OnDeactivate", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [EventArgs.Empty]);

        Assert.True(form.Visible);
        Assert.Single(form.Controls.Find("TitleBar", searchAllChildren: true));
        Button closeButton = Assert.IsType<Button>(Assert.Single(
            form.Controls.Find("CloseButton", searchAllChildren: true)));

        closeButton.PerformClick();
        Application.DoEvents();

        Assert.False(form.Visible);
        Assert.False(form.IsDisposed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_ActivationRefreshesTimeline() => StaTest.RunAsync(() =>
    {
        int queryCount = 0;
        using var form = new PopupForm(
            Path.GetTempPath(),
            (_, _) =>
            {
                queryCount++;
                return Task.FromResult<IReadOnlyList<ClipboardEntry>>([]);
            },
            static (_, _) => Task.CompletedTask,
            static (_, _, _) => Task.CompletedTask,
            static _ => Task.CompletedTask);

        typeof(Form)
            .GetMethod("OnActivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, [EventArgs.Empty]);

        Assert.Equal(1, queryCount);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopupForm_SingleClickSelectsAndDoubleClickActivates() => StaTest.RunAsync(() =>
    {
        int activationCount = 0;
        using PopupForm form = CreateInteractiveForm(() => activationCount++);
        BufferedListBox timeline = PrepareTimeline(form);
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

    [Fact]
    public Task PopupForm_DoubleClickingStarTogglesFavoriteOnceWithoutActivating() => StaTest.RunAsync(() =>
    {
        int activationCount = 0;
        int favoriteCount = 0;
        using PopupForm form = CreateInteractiveForm(() => activationCount++, () => favoriteCount++);
        BufferedListBox timeline = PrepareTimeline(form);
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

    private static BufferedListBox PrepareTimeline(PopupForm form)
    {
        form.CreateControl();
        BufferedListBox timeline = Assert.IsType<BufferedListBox>(form.Controls.OfType<BufferedListBox>().Single());
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

    private static void PumpUntil(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.True(condition());
    }
}
