using System.Drawing;
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
        Assert.False(form.ShowInTaskbar);
        Assert.Equal(new Size(560, 620), form.ClientSize);
        Assert.True(form.KeyPreview);
        return Task.CompletedTask;
    });
}
