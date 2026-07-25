using LocalClipboard.App.UI;
using System.Drawing;

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
