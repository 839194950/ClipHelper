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

        var result = new Bitmap(canvasSize.Width, canvasSize.Height, PixelFormat.Format32bppArgb);
        FillTransparent(result);
        using Graphics graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(source, new Rectangle(left, top, width, height));
        return result;
    }

    private static void FillTransparent(Bitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, Color.Transparent);
            }
        }
    }
}
