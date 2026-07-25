using LocalClipboard.App.UI;
using System.Drawing;

namespace LocalClipboard.App.IntegrationTests.UI;

public sealed class FavoriteStarRendererTests
{
    [Fact]
    public void StarPointsStayInsideBoundsAndHaveTenAlternatingVertices()
    {
        PointF[] points = FavoriteStarRenderer.CreatePoints(new Rectangle(100, 40, 30, 30));

        Assert.Equal(10, points.Length);
        Assert.All(points, point =>
        {
            Assert.InRange(point.X, 100, 130);
            Assert.InRange(point.Y, 40, 70);
        });
        Assert.NotEqual(points[0], points[2]);
        Assert.NotEqual(points[1], points[3]);
    }
}
