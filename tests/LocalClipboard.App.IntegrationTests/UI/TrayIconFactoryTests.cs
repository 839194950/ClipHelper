using LocalClipboard.App;
using System.Drawing;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class TrayIconFactoryTests
{
    [Fact]
    public void CreatesTheEmbeddedClipHelperIcon()
    {
        using Icon icon = TrayIconFactory.Create();

        Assert.NotNull(icon);
        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
    }
}
