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
        Assert.Equal(new Size(560, 620), form.ClientSize);
        Assert.True(form.KeyPreview);
        ListBox timeline = Assert.IsType<ListBox>(form.Controls.OfType<ListBox>().Single());
        Assert.Equal(92, timeline.ItemHeight);
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
}
