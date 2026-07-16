using System.ComponentModel;
using LocalClipboard.Core.Models;

namespace LocalClipboard.App.UI;

internal sealed class PopupForm : Form
{
    private const string FooterText = "↑↓ 选择  Enter 恢复  Delete 删除  Esc 关闭";

    private readonly string imageRoot;
    private readonly Func<HistoryQuery, CancellationToken, Task<IReadOnlyList<ClipboardEntry>>> queryEntries;
    private readonly Func<ClipboardEntry, CancellationToken, Task> deleteEntry;
    private readonly Func<Guid, bool, CancellationToken, Task> setFavorite;
    private readonly Func<ClipboardEntry, Task> activateEntry;
    private readonly ThemePalette palette = ThemePalette.ReadCurrent();
    private readonly TextBox searchBox = new();
    private readonly ListBox timeline = new();
    private readonly Label footer = new();
    private readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 150 };
    private readonly Dictionary<PopupFilter, Button> filterButtons = [];

    private PopupQueryState queryState = new(null, PopupFilter.All);
    private CancellationTokenSource? queryCancellation;
    private bool loading;
    private bool previousPageFull;
    private bool childDialogOpen;

    internal PopupForm(
        string imageRoot,
        Func<HistoryQuery, CancellationToken, Task<IReadOnlyList<ClipboardEntry>>> queryEntries,
        Func<ClipboardEntry, CancellationToken, Task> deleteEntry,
        Func<Guid, bool, CancellationToken, Task> setFavorite,
        Func<ClipboardEntry, Task> activateEntry)
    {
        this.imageRoot = Path.GetFullPath(imageRoot);
        this.queryEntries = queryEntries ?? throw new ArgumentNullException(nameof(queryEntries));
        this.deleteEntry = deleteEntry ?? throw new ArgumentNullException(nameof(deleteEntry));
        this.setFavorite = setFavorite ?? throw new ArgumentNullException(nameof(setFavorite));
        this.activateEntry = activateEntry ?? throw new ArgumentNullException(nameof(activateEntry));

        ClientSize = new Size(560, 620);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        BackColor = palette.Background;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        searchTimer.Tick += SearchTimer_Tick;
        searchBox.TextChanged += SearchBox_TextChanged;
        timeline.DrawItem += Timeline_DrawItem;
        timeline.MouseDown += Timeline_MouseDown;
        timeline.MouseWheel += Timeline_MouseWheel;
        timeline.SelectedIndexChanged += Timeline_SelectedIndexChanged;
        KeyDown += PopupForm_KeyDown;
        Deactivate += PopupForm_Deactivate;
    }

    internal static PopupForm CreateForTest() => new(
        Path.GetTempPath(),
        static (_, _) => Task.FromResult<IReadOnlyList<ClipboardEntry>>([]),
        static (_, _) => Task.CompletedTask,
        static (_, _, _) => Task.CompletedTask,
        static _ => Task.CompletedTask);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ChildDialogOpen
    {
        get => childDialogOpen;
        set => childDialogOpen = value;
    }

    internal void ShowPopup()
    {
        Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        int left = workingArea.Left + ((workingArea.Width - Width) / 2);
        int top = workingArea.Top + (int)Math.Round(workingArea.Height * 0.18);
        left = Math.Clamp(left, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - Width));
        top = Math.Clamp(top, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - Height));
        Location = new Point(left, top);

        if (!Visible) Show();
        Activate();
        searchBox.Focus();
        _ = RefreshAsync(append: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            searchTimer.Stop();
            searchTimer.Dispose();
            queryCancellation?.Cancel();
            queryCancellation = null;
            DisposeViews();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(14, 10, 14, 6),
            BackColor = palette.Surface
        };
        searchBox.BorderStyle = BorderStyle.None;
        searchBox.Dock = DockStyle.Fill;
        searchBox.Font = new Font(Font.FontFamily, 11F);
        searchBox.PlaceholderText = "搜索剪贴板历史…";
        searchBox.BackColor = palette.Surface;
        searchBox.ForeColor = palette.PrimaryText;
        topPanel.Controls.Add(searchBox);

        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(10, 4, 0, 4),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = palette.Surface
        };
        AddFilterButton(filterPanel, PopupFilter.All, "全部");
        AddFilterButton(filterPanel, PopupFilter.Text, "文本");
        AddFilterButton(filterPanel, PopupFilter.Images, "图片");
        AddFilterButton(filterPanel, PopupFilter.Favorites, "收藏");
        UpdateFilterButtons();

        footer.Dock = DockStyle.Bottom;
        footer.Height = 28;
        footer.Text = FooterText;
        footer.TextAlign = ContentAlignment.MiddleCenter;
        footer.BackColor = palette.Surface;
        footer.ForeColor = palette.SecondaryText;

        timeline.Dock = DockStyle.Fill;
        timeline.BorderStyle = BorderStyle.None;
        timeline.DrawMode = DrawMode.OwnerDrawFixed;
        timeline.IntegralHeight = false;
        timeline.ItemHeight = 78;
        timeline.BackColor = palette.Background;
        timeline.ForeColor = palette.PrimaryText;

        Controls.Add(timeline);
        Controls.Add(footer);
        Controls.Add(filterPanel);
        Controls.Add(topPanel);
    }

    private void AddFilterButton(Control parent, PopupFilter filter, string text)
    {
        var button = new Button
        {
            AutoSize = true,
            Height = 28,
            Text = text,
            Tag = filter,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 0, 4, 0),
            Padding = new Padding(10, 0, 10, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += FilterButton_Click;
        filterButtons.Add(filter, button);
        parent.Controls.Add(button);
    }

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        searchTimer.Stop();
        searchTimer.Start();
    }

    private void SearchTimer_Tick(object? sender, EventArgs e)
    {
        searchTimer.Stop();
        queryState = new PopupQueryState(searchBox.Text, queryState.Filter);
        _ = RefreshAsync(append: false);
    }

    private void FilterButton_Click(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: PopupFilter filter } || filter == queryState.Filter) return;
        queryState = new PopupQueryState(searchBox.Text, filter);
        UpdateFilterButtons();
        _ = RefreshAsync(append: false);
    }

    private void UpdateFilterButtons()
    {
        foreach ((PopupFilter filter, Button button) in filterButtons)
        {
            bool active = filter == queryState.Filter;
            button.BackColor = active ? palette.Selection : palette.Surface;
            button.ForeColor = active ? palette.Accent : palette.SecondaryText;
        }
    }

    private async Task RefreshAsync(bool append)
    {
        if (append && loading) return;

        queryCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        queryCancellation = cancellation;
        loading = true;

        PopupQueryState requestState = append
            ? queryState with { Offset = timeline.Items.Count }
            : queryState with { Offset = 0 };
        if (!append) queryState = requestState;

        try
        {
            IReadOnlyList<ClipboardEntry> entries = await queryEntries(
                requestState.ToHistoryQuery(),
                cancellation.Token);
            List<ClipboardEntryView> views = await Task.Run(
                () => CreateViews(entries, cancellation.Token),
                cancellation.Token);

            if (IsDisposed || !ReferenceEquals(queryCancellation, cancellation) || cancellation.IsCancellationRequested)
            {
                views.ForEach(view => view.Dispose());
                return;
            }

            timeline.BeginUpdate();
            try
            {
                if (!append) DisposeViews();
                foreach (ClipboardEntryView view in views) timeline.Items.Add(view);
                if (!append && timeline.Items.Count > 0) timeline.SelectedIndex = 0;
            }
            finally
            {
                timeline.EndUpdate();
            }

            previousPageFull = entries.Count == 100;
            footer.Text = FooterText;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!IsDisposed) footer.Text = "加载失败，请稍后重试";
        }
        finally
        {
            if (ReferenceEquals(queryCancellation, cancellation))
            {
                queryCancellation = null;
                loading = false;
            }
            cancellation.Dispose();
        }
    }

    private List<ClipboardEntryView> CreateViews(
        IReadOnlyList<ClipboardEntry> entries,
        CancellationToken cancellationToken)
    {
        var views = new List<ClipboardEntryView>(entries.Count);
        try
        {
            foreach (ClipboardEntry entry in entries) views.Add(CreateView(entry, cancellationToken));
            return views;
        }
        catch
        {
            views.ForEach(view => view.Dispose());
            throw;
        }
    }

    private ClipboardEntryView CreateView(ClipboardEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Image? thumbnail = entry.ContentType == ClipboardContentType.Image
            ? LoadThumbnail(entry.ThumbnailPath)
            : null;
        return new ClipboardEntryView(entry, thumbnail);
    }

    private Image? LoadThumbnail(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return null;

        try
        {
            string path = Path.GetFullPath(Path.Combine(imageRoot, relativePath));
            string rootPrefix = Path.TrimEndingDirectorySeparator(imageRoot) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;

            using Image source = Image.FromFile(path);
            return new Bitmap(source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    private void Timeline_DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= timeline.Items.Count) return;

        var view = (ClipboardEntryView)timeline.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color background = selected ? palette.Selection : palette.Surface;
        using var backgroundBrush = new SolidBrush(background);
        using var primaryBrush = new SolidBrush(palette.PrimaryText);
        using var secondaryBrush = new SolidBrush(palette.SecondaryText);
        using var accentBrush = new SolidBrush(palette.Accent);
        using var borderPen = new Pen(palette.Border);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        int textLeft = e.Bounds.Left + 14;
        if (view.Thumbnail is not null)
        {
            var imageBounds = new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 8, 58, 58);
            e.Graphics.DrawImage(view.Thumbnail, imageBounds);
            textLeft = imageBounds.Right + 12;
        }

        Rectangle starBounds = GetStarBounds(e.Bounds);
        string time = view.Entry.LastUsedAt.ToLocalTime().ToString("MM-dd HH:mm");
        e.Graphics.DrawString(time, Font, secondaryBrush, textLeft, e.Bounds.Top + 9);
        using var summaryFont = new Font(Font.FontFamily, 10F, FontStyle.Regular);
        var summaryBounds = new RectangleF(textLeft, e.Bounds.Top + 31, Math.Max(1, starBounds.Left - textLeft - 8), 38);
        e.Graphics.DrawString(view.DisplayText, summaryFont, primaryBrush, summaryBounds);
        e.Graphics.DrawString(view.Entry.IsFavorite ? "★" : "☆", summaryFont, accentBrush, starBounds);
        e.Graphics.DrawLine(borderPen, e.Bounds.Left + 10, e.Bounds.Bottom - 1, e.Bounds.Right - 10, e.Bounds.Bottom - 1);
        e.DrawFocusRectangle();
    }

    private async void Timeline_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int index = timeline.IndexFromPoint(e.Location);
        if (index == ListBox.NoMatches) return;
        timeline.SelectedIndex = index;
        var view = (ClipboardEntryView)timeline.Items[index];

        if (GetStarBounds(timeline.GetItemRectangle(index)).Contains(e.Location))
        {
            bool favorite = !view.Entry.IsFavorite;
            if (await TryRunUiActionAsync(() => setFavorite(view.Entry.Id, favorite, CancellationToken.None)))
            {
                view.UpdateFavorite(favorite);
                timeline.Invalidate(timeline.GetItemRectangle(index));
            }
            return;
        }

        if (await TryRunUiActionAsync(() => activateEntry(view.Entry))) Hide();
    }

    private async void PopupForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        ClipboardEntryView? selected = timeline.SelectedItem as ClipboardEntryView;
        if (selected is null) return;

        if (e.KeyCode == Keys.Enter)
        {
            if (await TryRunUiActionAsync(() => activateEntry(selected.Entry))) Hide();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            int index = timeline.SelectedIndex;
            if (await TryRunUiActionAsync(() => deleteEntry(selected.Entry, CancellationToken.None)))
            {
                timeline.Items.RemoveAt(index);
                selected.Dispose();
                if (timeline.Items.Count > 0) timeline.SelectedIndex = Math.Min(index, timeline.Items.Count - 1);
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void MoveSelection(int delta)
    {
        if (timeline.Items.Count == 0) return;
        int current = timeline.SelectedIndex < 0 ? 0 : timeline.SelectedIndex;
        timeline.SelectedIndex = Math.Clamp(current + delta, 0, timeline.Items.Count - 1);
    }

    private void Timeline_MouseWheel(object? sender, MouseEventArgs e) => TryLoadNextPage();

    private void Timeline_SelectedIndexChanged(object? sender, EventArgs e) => TryLoadNextPage();

    private void TryLoadNextPage()
    {
        if (!previousPageFull || loading || timeline.Items.Count == 0) return;
        int lastVisible = Math.Max(timeline.SelectedIndex, timeline.TopIndex + Math.Max(1, timeline.ClientSize.Height / timeline.ItemHeight));
        if (lastVisible >= timeline.Items.Count - 5) _ = RefreshAsync(append: true);
    }

    private async Task<bool> TryRunUiActionAsync(Func<Task> action)
    {
        try
        {
            await action();
            footer.Text = FooterText;
            return true;
        }
        catch (Exception)
        {
            footer.Text = "操作失败，请稍后重试";
            return false;
        }
    }

    private void PopupForm_Deactivate(object? sender, EventArgs e)
    {
        if (!childDialogOpen) Hide();
    }

    private static Rectangle GetStarBounds(Rectangle itemBounds) => new(
        itemBounds.Right - 42,
        itemBounds.Top + 22,
        30,
        30);

    private void DisposeViews()
    {
        foreach (ClipboardEntryView view in timeline.Items.OfType<ClipboardEntryView>()) view.Dispose();
        timeline.Items.Clear();
    }
}
