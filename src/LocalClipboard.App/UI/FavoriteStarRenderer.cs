using System.Drawing.Drawing2D;

namespace LocalClipboard.App.UI;

internal static class FavoriteStarRenderer
{
    internal static PointF[] CreatePoints(Rectangle bounds)
    {
        float centerX = bounds.Left + (bounds.Width / 2f);
        float centerY = bounds.Top + (bounds.Height / 2f);
        float outerRadius = Math.Min(bounds.Width, bounds.Height) * 0.42f;
        float innerRadius = outerRadius * 0.42f;
        var points = new PointF[10];

        for (int index = 0; index < points.Length; index++)
        {
            double angle = (-Math.PI / 2d) + (index * Math.PI / 5d);
            float radius = index % 2 == 0 ? outerRadius : innerRadius;
            points[index] = new PointF(
                centerX + (float)(Math.Cos(angle) * radius),
                centerY + (float)(Math.Sin(angle) * radius));
        }

        return points;
    }

    internal static void Draw(Graphics graphics, Rectangle bounds, bool filled, Brush brush, Pen pen)
    {
        PointF[] points = CreatePoints(bounds);
        if (filled)
        {
            graphics.FillPolygon(brush, points);
            graphics.DrawPolygon(pen, points);
            return;
        }

        graphics.DrawPolygon(pen, points);
    }
}
