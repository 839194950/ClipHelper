using System.Windows.Forms;

namespace LocalClipboard.App.UI;

internal sealed class PopupListRenderer : IDisposable
{
    private readonly ThemePalette palette;
    private readonly SolidBrush listBrush;
    private readonly SolidBrush surfaceBrush;
    private readonly SolidBrush hoverBrush;
    private readonly SolidBrush selectionBrush;
    private readonly SolidBrush shadowBrush = new(Color.FromArgb(24, Color.Black));
    private readonly SolidBrush accentBrush;
    private readonly Pen borderPen;
    private readonly Pen hoverBorderPen;
    private readonly Pen selectionBorderPen;
    private readonly Pen favoriteStarPen;
    private readonly Font summaryFont = new("Segoe UI", 10F, FontStyle.Regular);

    internal PopupListRenderer(ThemePalette palette)
    {
        this.palette = palette;
        listBrush = new SolidBrush(palette.Background);
        surfaceBrush = new SolidBrush(palette.Surface);
        hoverBrush = new SolidBrush(palette.MutedSurface);
        selectionBrush = new SolidBrush(palette.Selection);
        accentBrush = new SolidBrush(palette.Accent);
        borderPen = new Pen(palette.Border);
        hoverBorderPen = new Pen(palette.HoverBorder);
        selectionBorderPen = new Pen(palette.Accent);
        favoriteStarPen = new Pen(palette.Accent, 1.4F);
    }

    internal void Draw(Graphics graphics, Rectangle bounds, ClipboardEntryView view, Font baseFont, bool selected, bool hovered)
    {
        graphics.FillRectangle(listBrush, bounds);

        Rectangle card = new(bounds.Left + 10, bounds.Top + 6, Math.Max(1, bounds.Width - 20), Math.Max(1, bounds.Height - 12));
        if (selected || hovered)
        {
            Rectangle shadow = new(card.Left + 1, card.Top + 2, card.Width, card.Height);
            graphics.FillRectangle(shadowBrush, shadow);
        }

        Brush cardBrush = selected ? selectionBrush : hovered ? hoverBrush : surfaceBrush;
        Pen cardBorder = selected ? selectionBorderPen : hovered ? hoverBorderPen : borderPen;
        graphics.FillRectangle(cardBrush, card);
        graphics.DrawRectangle(cardBorder, card);

        int textLeft = card.Left + 14;
        if (view.Thumbnail is not null)
        {
            Rectangle imageBounds = new(card.Left + 12, card.Top + 12, 58, 58);
            graphics.FillRectangle(hoverBrush, imageBounds);
            graphics.DrawRectangle(borderPen, imageBounds);
            graphics.DrawImage(view.Thumbnail, imageBounds);
            textLeft = imageBounds.Right + 12;
        }

        Rectangle starBounds = new(bounds.Right - 52, bounds.Top + 26, 30, 30);
        TextRenderer.DrawText(
            graphics, view.DisplayTime, baseFont,
            new Rectangle(textLeft, card.Top + 8, starBounds.Left - textLeft - 8, 20),
            palette.SecondaryText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        RectangleF summary = PopupForm.GetSummaryBounds(bounds, textLeft, starBounds.Left);
        TextRenderer.DrawText(
            graphics, view.DisplayText, summaryFont, Rectangle.Round(summary), palette.PrimaryText,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl);
        FavoriteStarRenderer.Draw(graphics, starBounds, view.Entry.IsFavorite, accentBrush, favoriteStarPen);
    }

    public void Dispose()
    {
        listBrush.Dispose();
        surfaceBrush.Dispose();
        hoverBrush.Dispose();
        selectionBrush.Dispose();
        shadowBrush.Dispose();
        accentBrush.Dispose();
        borderPen.Dispose();
        hoverBorderPen.Dispose();
        selectionBorderPen.Dispose();
        favoriteStarPen.Dispose();
        summaryFont.Dispose();
    }
}
