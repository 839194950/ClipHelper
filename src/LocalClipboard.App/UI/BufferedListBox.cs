using System.Collections;
using System.Diagnostics;

namespace LocalClipboard.App.UI;

internal sealed class BufferedListBox : Control
{
    internal const int NoMatches = -1;
    private const int ScrollTimerPeriodMilliseconds = 8;
    private const int ScrollbarMargin = 6;
    private const int ScrollbarNormalWidth = 4;
    private const int ScrollbarActiveWidth = 7;
    private const int ScrollbarHitWidth = 14;
    private readonly TimelineItemCollection items;
    private readonly SmoothScrollPhysics scrollPhysics = new();
    private System.Threading.Timer? scrollTimer;
    private int itemHeight = 92;
    private int selectedIndex = -1;
    private int scrollOffset;
    private int scrollTarget;
    private int updateDepth;
    private long lastFrameTimestamp;
    private int framePosted;
    private double wheelRemainder;
    private bool scrollAnimating;
    private bool scrollbarHovered;
    private bool scrollbarDragging;
    private int scrollbarDragOffset;

    internal BufferedListBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Opaque,
            true);
        DoubleBuffered = true;
        items = new TimelineItemCollection(this);
    }

    internal TimelineItemCollection Items => items;
    internal bool UsesOptimizedRendering => DoubleBuffered && GetStyle(ControlStyles.OptimizedDoubleBuffer);
    internal bool IsScrollAnimating => scrollAnimating;
    internal bool IsScrollbarDragging => scrollbarDragging;
    internal int ScrollOffset => scrollOffset;
    internal int ScrollTarget => scrollTarget;
    internal int TopIndex => ItemHeight == 0 ? 0 : scrollOffset / ItemHeight;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool IntegralHeight { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal BorderStyle BorderStyle { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal DrawMode DrawMode { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int ItemHeight
    {
        get => itemHeight;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            itemHeight = value;
            SetScrollOffset(scrollOffset);
            Invalidate();
        }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            int normalized = value < 0 || value >= Items.Count ? -1 : value;
            if (normalized == selectedIndex) return;
            int previous = selectedIndex;
            selectedIndex = normalized;
            if (previous >= 0) Invalidate(GetItemRectangle(previous));
            if (selectedIndex >= 0)
            {
                EnsureVisible(selectedIndex);
                Invalidate(GetItemRectangle(selectedIndex));
            }
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal ClipboardEntryView? SelectedItem =>
        selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;

    internal event DrawItemEventHandler? DrawItem;
    internal event EventHandler? SelectedIndexChanged;
    internal event EventHandler? ScrollPositionChanged;

    internal void BeginUpdate() => updateDepth++;

    internal void EndUpdate()
    {
        if (updateDepth > 0) updateDepth--;
        if (updateDepth == 0)
        {
            SetScrollOffset(scrollOffset);
            Invalidate();
        }
    }

    internal Rectangle GetItemRectangle(int index)
    {
        if (index < 0 || index >= Items.Count) return Rectangle.Empty;
        return new Rectangle(0, (index * ItemHeight) - scrollOffset, ClientSize.Width, ItemHeight);
    }

    internal int IndexFromPoint(Point location)
    {
        if (!ClientRectangle.Contains(location)) return NoMatches;
        int index = (location.Y + scrollOffset) / ItemHeight;
        return index >= 0 && index < Items.Count ? index : NoMatches;
    }

    internal int IndexFromPoint(int x, int y) => IndexFromPoint(new Point(x, y));

    internal void RequestWheelScroll(int delta)
    {
        if (Items.Count == 0 || delta == 0) return;
        int lines = SystemInformation.MouseWheelScrollLines;
        double pixelsPerDetent = lines < 0
            ? Math.Max(ClientSize.Height, ItemHeight)
            : Math.Max(24d, ItemHeight * Math.Clamp(lines, 1, 8) / 3d);
        wheelRemainder += (-delta / (double)SystemInformation.MouseWheelScrollDelta) * pixelsPerDetent;
        int movement = (int)Math.Truncate(wheelRemainder);
        wheelRemainder -= movement;
        if (movement == 0) return;

        int baseTarget = scrollAnimating ? scrollTarget : scrollOffset;
        RequestScrollTarget(baseTarget + movement);
    }

    internal void RequestScrollTarget(int target)
    {
        if (!scrollAnimating)
        {
            scrollPhysics.SetPosition(scrollOffset);
        }
        scrollTarget = Math.Clamp(target, 0, MaximumScrollOffset);
        if (scrollTarget == scrollOffset)
        {
            StopScrollAnimation();
            return;
        }

        scrollPhysics.SetTarget(scrollTarget);
        StartScrollAnimation();
    }

    internal Rectangle GetScrollbarThumbBounds()
    {
        if (MaximumScrollOffset == 0) return Rectangle.Empty;
        int trackHeight = Math.Max(1, ClientSize.Height - (ScrollbarMargin * 2));
        int contentHeight = Items.Count * ItemHeight;
        int thumbHeight = Math.Max(32, (int)Math.Round(trackHeight * (ClientSize.Height / (double)contentHeight)));
        int travel = Math.Max(1, trackHeight - thumbHeight);
        int thumbTop = ScrollbarMargin + (int)Math.Round(travel * (scrollOffset / (double)MaximumScrollOffset));
        int width = scrollbarHovered || scrollbarDragging ? ScrollbarActiveWidth : ScrollbarNormalWidth;
        return new Rectangle(ClientSize.Width - width - 3, thumbTop, width, thumbHeight);
    }

    internal bool BeginScrollbarInteraction(Point location)
    {
        Rectangle thumb = GetScrollbarThumbBounds();
        if (thumb == Rectangle.Empty || location.X < ClientSize.Width - ScrollbarHitWidth) return false;

        StopScrollAnimation();
        if (GetScrollbarHitBounds().Contains(location))
        {
            scrollbarDragging = true;
            scrollbarDragOffset = location.Y - thumb.Top;
            Capture = true;
        }
        else
        {
            int direction = location.Y < thumb.Top ? -1 : 1;
            RequestScrollTarget(scrollOffset + (direction * ClientSize.Height));
        }
        Invalidate();
        return true;
    }

    internal void UpdateScrollbarDrag(Point location)
    {
        if (!scrollbarDragging) return;
        Rectangle thumb = GetScrollbarThumbBounds();
        int trackTop = ScrollbarMargin;
        int travel = Math.Max(1, ClientSize.Height - (ScrollbarMargin * 2) - thumb.Height);
        int thumbTop = Math.Clamp(location.Y - scrollbarDragOffset, trackTop, trackTop + travel);
        double ratio = (thumbTop - trackTop) / (double)travel;
        SetScrollOffset((int)Math.Round(MaximumScrollOffset * ratio));
    }

    internal void EndScrollbarInteraction()
    {
        scrollbarDragging = false;
        Capture = false;
        Invalidate();
    }

    internal void AdvanceScrollAnimation(TimeSpan delta)
    {
        if (!scrollAnimating) return;
        scrollPhysics.Advance(delta);
        SetScrollOffset((int)Math.Round(scrollPhysics.Position), synchronizePhysics: false);
        if (scrollPhysics.IsSettled)
        {
            SetScrollOffset(scrollTarget, synchronizePhysics: false);
            StopScrollAnimation();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        RequestWheelScroll(e.Delta);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && BeginScrollbarInteraction(e.Location)) return;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (scrollbarDragging)
        {
            UpdateScrollbarDrag(e.Location);
            return;
        }

        bool hovered = GetScrollbarHitBounds().Contains(e.Location);
        if (hovered != scrollbarHovered)
        {
            scrollbarHovered = hovered;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (scrollbarDragging && e.Button == MouseButtons.Left)
        {
            EndScrollbarInteraction();
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (scrollbarHovered)
        {
            scrollbarHovered = false;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (scrollbarDragging)
        {
            scrollbarDragging = false;
            Invalidate();
        }
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (Items.Count == 0) return;

        int first = Math.Max(0, TopIndex);
        int last = Math.Min(Items.Count - 1, (scrollOffset + ClientSize.Height) / ItemHeight);
        for (int index = first; index <= last; index++)
        {
            Rectangle bounds = GetItemRectangle(index);
            DrawItemState state = index == selectedIndex ? DrawItemState.Selected : DrawItemState.None;
            DrawItem?.Invoke(this, new DrawItemEventArgs(
                e.Graphics, Font, bounds, index, state, ForeColor, BackColor));
        }

        DrawScrollbar(e.Graphics);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopScrollAnimation();
            scrollTimer?.Dispose();
            scrollTimer = null;
        }
        base.Dispose(disposing);
    }

    internal int MaximumScrollOffset => Math.Max(0, (Items.Count * ItemHeight) - ClientSize.Height);

    private bool SetScrollOffset(int value, bool synchronizePhysics = true)
    {
        int normalized = Math.Clamp(value, 0, MaximumScrollOffset);
        if (normalized == scrollOffset)
        {
            if (synchronizePhysics)
            {
                scrollPhysics.SetPosition(normalized);
                scrollTarget = normalized;
            }
            return false;
        }
        scrollOffset = normalized;
        if (synchronizePhysics)
        {
            scrollPhysics.SetPosition(normalized);
            scrollTarget = normalized;
        }
        Invalidate();
        ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void EnsureVisible(int index)
    {
        Rectangle bounds = GetItemRectangle(index);
        if (bounds.Top < 0) SetScrollOffset(index * ItemHeight);
        else if (bounds.Bottom > ClientSize.Height) SetScrollOffset(((index + 1) * ItemHeight) - ClientSize.Height);
    }

    private void StopScrollAnimation()
    {
        scrollAnimating = false;
        scrollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        lastFrameTimestamp = 0;
    }

    private void StartScrollAnimation()
    {
        scrollAnimating = true;
        lastFrameTimestamp = Stopwatch.GetTimestamp();
        scrollTimer ??= new System.Threading.Timer(ScrollTimerCallback);
        scrollTimer.Change(0, ScrollTimerPeriodMilliseconds);
    }

    private void ScrollTimerCallback(object? state)
    {
        if (Interlocked.Exchange(ref framePosted, 1) != 0) return;
        if (IsDisposed || !IsHandleCreated)
        {
            Interlocked.Exchange(ref framePosted, 0);
            return;
        }

        try
        {
            BeginInvoke((Action)(() =>
            {
                Interlocked.Exchange(ref framePosted, 0);
                if (!scrollAnimating) return;

                long now = Stopwatch.GetTimestamp();
                TimeSpan delta = lastFrameTimestamp == 0
                    ? TimeSpan.FromMilliseconds(ScrollTimerPeriodMilliseconds)
                    : Stopwatch.GetElapsedTime(lastFrameTimestamp, now);
                lastFrameTimestamp = now;
                AdvanceScrollAnimation(delta);
            }));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref framePosted, 0);
        }
    }

    private void DrawScrollbar(Graphics graphics)
    {
        Rectangle thumb = GetScrollbarThumbBounds();
        if (thumb == Rectangle.Empty) return;

        using var brush = new SolidBrush(Color.FromArgb(80, ForeColor));
        graphics.FillRectangle(brush, thumb);
    }

    private Rectangle GetScrollbarHitBounds()
    {
        Rectangle thumb = GetScrollbarThumbBounds();
        return thumb == Rectangle.Empty
            ? Rectangle.Empty
            : new Rectangle(ClientSize.Width - ScrollbarHitWidth, thumb.Top, ScrollbarHitWidth, thumb.Height);
    }

    private void ItemsChanged()
    {
        if (selectedIndex >= Items.Count) selectedIndex = Items.Count - 1;
        SetScrollOffset(scrollOffset);
        if (updateDepth == 0) Invalidate();
    }

    internal sealed class TimelineItemCollection(BufferedListBox owner) : IList<ClipboardEntryView>
    {
        private readonly List<ClipboardEntryView> values = [];
        public ClipboardEntryView this[int index] { get => values[index]; set { values[index] = value; owner.ItemsChanged(); } }
        public int Count => values.Count;
        public bool IsReadOnly => false;
        public void Add(ClipboardEntryView item) { values.Add(item); owner.ItemsChanged(); }
        public void Clear() { values.Clear(); owner.ItemsChanged(); }
        public bool Contains(ClipboardEntryView item) => values.Contains(item);
        public void CopyTo(ClipboardEntryView[] array, int arrayIndex) => values.CopyTo(array, arrayIndex);
        public IEnumerator<ClipboardEntryView> GetEnumerator() => values.GetEnumerator();
        public int IndexOf(ClipboardEntryView item) => values.IndexOf(item);
        public void Insert(int index, ClipboardEntryView item) { values.Insert(index, item); owner.ItemsChanged(); }
        public bool Remove(ClipboardEntryView item) { bool removed = values.Remove(item); if (removed) owner.ItemsChanged(); return removed; }
        public void RemoveAt(int index) { values.RemoveAt(index); owner.ItemsChanged(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
