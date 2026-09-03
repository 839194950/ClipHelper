using System.ComponentModel;
using System.Runtime.InteropServices;
using LocalClipboard.App;
using LocalClipboard.Core.Models;
using LocalClipboard.Infrastructure.Settings;

namespace LocalClipboard.App.UI;

internal sealed class PopupForm : Form
{
    private const int TimelineItemHeight = 92;
    private const int SummaryTopOffset = 31;
    private const int SummaryHeight = 44;
    private const int PreferredPopupHeight = 1200;
    private const int PopupScreenMargin = 32;
    private const int WmNcLButtonDown = 0x00A1;
    private static readonly nint HtCaption = new(2);

    private readonly string imageRoot;
    private readonly Func<HistoryQuery, CancellationToken, Task<IReadOnlyList<ClipboardEntry>>> queryEntries;
    private readonly Func<ClipboardEntry, CancellationToken, Task> deleteEntry;
    private readonly Func<Guid, bool, CancellationToken, Task> setFavorite;
    private readonly Func<ClipboardEntry, Task> activateEntry;
    private readonly Action openSettings;
    private readonly ThemePalette palette = ThemePalette.ReadCurrent();
    private readonly PopupListRenderer listRenderer;
    private readonly TextBox searchBox = new();
    private readonly Button clearSearchButton = new();
    private readonly BufferedListBox timeline = new();
    private readonly Label footer = new();
    private readonly System.Windows.Forms.Timer searchTimer = new() { Interval = 150 };
    private readonly Dictionary<PopupFilter, Button> filterButtons = [];

    private PopupQueryState queryState = new(null, PopupFilter.All);
    private CancellationTokenSource? queryCancellation;
    private bool loading;
    private AppLanguage language = AppLanguage.Chinese;
    private bool previousPageFull;
    private int hoveredIndex = -1;

    internal PopupForm(
        string imageRoot,
        Func<HistoryQuery, CancellationToken, Task<IReadOnlyList<ClipboardEntry>>> queryEntries,
        Func<ClipboardEntry, CancellationToken, Task> deleteEntry,
        Func<Guid, bool, CancellationToken, Task> setFavorite,
        Func<ClipboardEntry, Task> activateEntry,
        Action? openSettings = null)
    {
        this.imageRoot = Path.GetFullPath(imageRoot);
        this.queryEntries = queryEntries ?? throw new ArgumentNullException(nameof(queryEntries));
        this.deleteEntry = deleteEntry ?? throw new ArgumentNullException(nameof(deleteEntry));
        this.setFavorite = setFavorite ?? throw new ArgumentNullException(nameof(setFavorite));
        this.activateEntry = activateEntry ?? throw new ArgumentNullException(nameof(activateEntry));
        this.openSettings = openSettings ?? (() => { });
        listRenderer = new PopupListRenderer(palette);

        ClientSize = new Size(600, PreferredPopupHeight);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        Icon = TrayIconFactory.Create();
        Text = "ClipHelper";
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        BackColor = palette.Background;
        Font = new Font("Segoe UI", 9F);
        Padding = new Padding(1);
        DoubleBuffered = true;
        Paint += PopupForm_Paint;

        searchBox.TextChanged += SearchBox_TextChanged;
        BuildLayout();
        searchTimer.Tick += SearchTimer_Tick;
        timeline.DrawItem += Timeline_DrawItem;
        timeline.MouseDown += Timeline_MouseDown;
        timeline.MouseDoubleClick += Timeline_MouseDoubleClick;
        timeline.MouseWheel += Timeline_MouseWheel;
        timeline.MouseMove += Timeline_MouseMove;
        timeline.MouseLeave += Timeline_MouseLeave;
        timeline.SelectedIndexChanged += Timeline_SelectedIndexChanged;
        timeline.ScrollPositionChanged += Timeline_ScrollPositionChanged;
        KeyDown += PopupForm_KeyDown;
        Activated += PopupForm_Activated;
    }

    internal static PopupForm CreateForTest(Action? openSettings = null) => new(
        Path.GetTempPath(),
        static (_, _) => Task.FromResult<IReadOnlyList<ClipboardEntry>>([]),
        static (_, _) => Task.CompletedTask,
        static (_, _, _) => Task.CompletedTask,
        static _ => Task.CompletedTask,
        openSettings);

    internal void ShowPopup()
    {
        Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Height = Math.Min(PreferredPopupHeight, Math.Max(480, workingArea.Height - PopupScreenMargin));
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

    internal void ApplyLanguage(AppLanguage language)
    {
        this.language = language;
        bool english = language == AppLanguage.English;
        searchBox.PlaceholderText = language == AppLanguage.English ? "Search clipboard history..." : "搜索剪贴板历史…";
        searchBox.AccessibleName = language == AppLanguage.English ? "Search" : "搜索";
        clearSearchButton.AccessibleName = language == AppLanguage.English ? "Clear search" : "清除搜索";
        footer.Text = english
            ? "Up/Down select  Double-click/Enter restore  Delete remove  Esc close"
            : "↑↓ 选择  双击/Enter 恢复  Delete 删除  Esc 关闭";
        if (Controls.Find("SettingsButton", true).FirstOrDefault() is Button settingsButton)
            settingsButton.AccessibleName = english ? "Open settings" : "打开设置";
        if (filterButtons.TryGetValue(PopupFilter.All, out Button? allButton)) allButton.Text = english ? "All" : "全部";
        if (filterButtons.TryGetValue(PopupFilter.Text, out Button? textButton)) textButton.Text = english ? "Text" : "文本";
        if (filterButtons.TryGetValue(PopupFilter.Images, out Button? imagesButton)) imagesButton.Text = english ? "Images" : "图片";
        if (filterButtons.TryGetValue(PopupFilter.Favorites, out Button? favoritesButton)) favoritesButton.Text = english ? "Favorites" : "收藏";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            searchTimer.Stop();
            searchTimer.Dispose();
            listRenderer.Dispose();
            queryCancellation?.Cancel();
            queryCancellation = null;
            DisposeViews();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var titleBar = new Panel
        {
            Name = "TitleBar",
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = palette.Surface
        };
        var titleLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 0, 0),
            Text = "ClipHelper",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = palette.PrimaryText,
            Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold)
        };
        var closeButton = new Button
        {
            Name = "CloseButton",
            Dock = DockStyle.Right,
            Width = 46,
            Text = "×",
            FlatStyle = FlatStyle.Flat,
            BackColor = palette.Surface,
            ForeColor = palette.SecondaryText,
            Font = new Font(Font.FontFamily, 14F),
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        closeButton.FlatAppearance.BorderSize = 0;
        closeButton.Click += (_, _) => Hide();
        closeButton.MouseEnter += (_, _) => closeButton.BackColor = palette.Selection;
        closeButton.MouseLeave += (_, _) => closeButton.BackColor = palette.Surface;
        var settingsButton = new Button
        {
            Name = "SettingsButton",
            Dock = DockStyle.Right,
            Width = 46,
            Text = "⚙",
            FlatStyle = FlatStyle.Flat,
            BackColor = palette.Surface,
            ForeColor = palette.SecondaryText,
            Font = new Font("Segoe UI Symbol", 11F),
            TabStop = false,
            UseVisualStyleBackColor = false,
            AccessibleName = "打开设置"
        };
        settingsButton.FlatAppearance.BorderSize = 0;
        settingsButton.Click += (_, _) => this.openSettings();
        settingsButton.MouseEnter += (_, _) => settingsButton.BackColor = palette.Selection;
        settingsButton.MouseLeave += (_, _) => settingsButton.BackColor = palette.Surface;
        titleBar.MouseDown += TitleBar_MouseDown;
        titleLabel.MouseDown += TitleBar_MouseDown;
        var titleAccent = new Panel
        {
            Name = "TitleAccent",
            Dock = DockStyle.Left,
            Width = 5,
            BackColor = palette.Accent
        };
        titleBar.Controls.Add(titleLabel);
        titleBar.Controls.Add(settingsButton);
        titleBar.Controls.Add(closeButton);
        titleBar.Controls.Add(titleAccent);
        titleBar.Paint += (_, e) => DrawSeparator(e.Graphics, titleBar.ClientRectangle.Bottom - 1);

        var topPanel = new Panel
        {
            Name = "SearchPanel",
            Dock = DockStyle.Top,
            Height = 58,
            Padding = new Padding(14, 10, 14, 8),
            BackColor = palette.Background
        };
        searchBox.Name = "SearchBox";
        searchBox.BorderStyle = BorderStyle.FixedSingle;
        searchBox.Dock = DockStyle.Fill;
        searchBox.Font = new Font(Font.FontFamily, 11F);
        searchBox.PlaceholderText = "搜索剪贴板历史…";
        searchBox.BackColor = palette.Surface;
        searchBox.ForeColor = palette.PrimaryText;
        searchBox.Padding = new Padding(8, 0, 8, 0);
        var searchHost = new Panel
        {
            Name = "SearchHost",
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = palette.Border
        };
        clearSearchButton.Name = "ClearSearchButton";
        clearSearchButton.Dock = DockStyle.Right;
        clearSearchButton.Width = 38;
        clearSearchButton.Text = "×";
        clearSearchButton.FlatStyle = FlatStyle.Flat;
        clearSearchButton.BackColor = palette.Surface;
        clearSearchButton.ForeColor = palette.SecondaryText;
        clearSearchButton.Font = new Font(Font.FontFamily, 12F);
        clearSearchButton.TabStop = false;
        clearSearchButton.Visible = false;
        clearSearchButton.UseVisualStyleBackColor = false;
        clearSearchButton.AccessibleName = "清除搜索";
        clearSearchButton.FlatAppearance.BorderSize = 0;
        clearSearchButton.Click += (_, _) =>
        {
            searchBox.Clear();
            searchBox.Focus();
        };
        clearSearchButton.MouseEnter += (_, _) => clearSearchButton.BackColor = palette.Selection;
        clearSearchButton.MouseLeave += (_, _) => clearSearchButton.BackColor = palette.Surface;
        searchHost.Controls.Add(searchBox);
        searchHost.Controls.Add(clearSearchButton);
        topPanel.Controls.Add(searchHost);
        topPanel.Paint += (_, e) => DrawSeparator(e.Graphics, topPanel.ClientRectangle.Bottom - 1);

        var filterPanel = new FlowLayoutPanel
        {
            Name = "FilterPanel",
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(12, 5, 0, 5),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = palette.Surface
        };
        AddFilterButton(filterPanel, PopupFilter.All, "全部");
        AddFilterButton(filterPanel, PopupFilter.Text, "文本");
        AddFilterButton(filterPanel, PopupFilter.Images, "图片");
        AddFilterButton(filterPanel, PopupFilter.Favorites, "收藏");
        UpdateFilterButtons();
        filterPanel.Paint += (_, e) => DrawSeparator(e.Graphics, filterPanel.ClientRectangle.Bottom - 1);

        footer.Dock = DockStyle.Bottom;
        footer.Height = 32;
        ApplyLanguage(language);
        footer.TextAlign = ContentAlignment.MiddleCenter;
        footer.BackColor = palette.Surface;
        footer.ForeColor = palette.SecondaryText;

        timeline.Name = "Timeline";
        timeline.Dock = DockStyle.Fill;
        timeline.BorderStyle = BorderStyle.None;
        timeline.DrawMode = DrawMode.OwnerDrawFixed;
        timeline.IntegralHeight = false;
        timeline.ItemHeight = TimelineItemHeight;
        timeline.BackColor = palette.Background;
        timeline.ForeColor = palette.PrimaryText;

        Controls.Add(timeline);
        Controls.Add(footer);
        Controls.Add(filterPanel);
        Controls.Add(topPanel);
        Controls.Add(titleBar);
    }

    private void AddFilterButton(Control parent, PopupFilter filter, string text)
    {
        var button = new Button
        {
            Name = $"FilterButton_{filter}",
            AutoSize = true,
            Height = 30,
            Text = text,
            Tag = filter,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(2, 0, 6, 0),
            Padding = new Padding(12, 0, 12, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        button.MouseEnter += (_, _) =>
        {
            if (button.Tag is PopupFilter filter && filter != queryState.Filter)
            {
                button.BackColor = palette.Selection;
                button.ForeColor = palette.PrimaryText;
            }
        };
        button.MouseLeave += (_, _) => UpdateFilterButtons();
        button.Click += FilterButton_Click;
        filterButtons.Add(filter, button);
        parent.Controls.Add(button);
    }

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        clearSearchButton.Visible = searchBox.TextLength > 0;
        searchTimer.Stop();
        searchTimer.Start();
    }

    private void PopupForm_Activated(object? sender, EventArgs e) => _ = RefreshAsync(append: false);

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
            ApplyLanguage(language);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (!IsDisposed) footer.Text = language == AppLanguage.English ? "Loading failed. Please try again." : "加载失败，请稍后重试";
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
            return ThumbnailScaler.CreateListThumbnail(source, new Size(58, 58));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    private void Timeline_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= timeline.Items.Count)
        {
            e.DrawBackground();
            return;
        }

        var view = (ClipboardEntryView)timeline.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool hovered = e.Index == hoveredIndex;
        listRenderer.Draw(e.Graphics, e.Bounds, view, Font, selected, hovered);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= 0x00020000;
            return parameters;
        }
    }

    private void PopupForm_Paint(object? sender, PaintEventArgs e)
    {
        using var borderPen = new Pen(palette.Border);
        e.Graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private void DrawSeparator(Graphics graphics, int y)
    {
        using var separatorPen = new Pen(palette.Border);
        graphics.DrawLine(separatorPen, 0, y, Math.Max(0, ClientSize.Width - 1), y);
    }

    private void Timeline_MouseMove(object? sender, MouseEventArgs e)
    {
        int index = timeline.IndexFromPoint(e.Location);
        if (index == hoveredIndex) return;
        int previous = hoveredIndex;
        hoveredIndex = index == BufferedListBox.NoMatches ? -1 : index;
        if (previous >= 0) timeline.Invalidate(timeline.GetItemRectangle(previous));
        if (hoveredIndex >= 0) timeline.Invalidate(timeline.GetItemRectangle(hoveredIndex));
    }

    private void Timeline_MouseLeave(object? sender, EventArgs e)
    {
        if (hoveredIndex < 0) return;
        int previous = hoveredIndex;
        hoveredIndex = -1;
        timeline.Invalidate(timeline.GetItemRectangle(previous));
    }

    private async void Timeline_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int index = timeline.IndexFromPoint(e.Location);
        if (index == BufferedListBox.NoMatches) return;
        timeline.SelectedIndex = index;
        var view = (ClipboardEntryView)timeline.Items[index];

        if (GetStarBounds(timeline.GetItemRectangle(index)).Contains(e.Location))
        {
            if (e.Clicks > 1) return;
            bool favorite = !view.Entry.IsFavorite;
            if (await TryRunUiActionAsync(() => setFavorite(view.Entry.Id, favorite, CancellationToken.None)))
            {
                view.UpdateFavorite(favorite);
                timeline.Invalidate(timeline.GetItemRectangle(index));
            }
        }
    }

    private async void Timeline_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int index = timeline.IndexFromPoint(e.Location);
        if (index == BufferedListBox.NoMatches) return;
        Rectangle itemBounds = timeline.GetItemRectangle(index);
        if (GetStarBounds(itemBounds).Contains(e.Location)) return;

        timeline.SelectedIndex = index;
        var view = (ClipboardEntryView)timeline.Items[index];
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

    private void Timeline_MouseWheel(object? sender, MouseEventArgs e)
    {
        if (hoveredIndex >= 0)
        {
            int previous = hoveredIndex;
            hoveredIndex = -1;
            timeline.Invalidate(timeline.GetItemRectangle(previous));
        }
    }

    private void Timeline_SelectedIndexChanged(object? sender, EventArgs e) => TryLoadNextPage();

    private void Timeline_ScrollPositionChanged(object? sender, EventArgs e) => TryLoadNextPage();

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
            ApplyLanguage(language);
            return true;
        }
        catch (Exception)
        {
            footer.Text = language == AppLanguage.English ? "Operation failed. Please try again." : "操作失败，请稍后重试";
            return false;
        }
    }

    private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, HtCaption, nint.Zero);
    }

    private static Rectangle GetStarBounds(Rectangle itemBounds) => new(
        itemBounds.Right - 42,
        itemBounds.Top + 22,
        30,
        30);

    internal static RectangleF GetSummaryBounds(Rectangle itemBounds, int textLeft, int starLeft) => new(
        textLeft,
        itemBounds.Top + SummaryTopOffset,
        Math.Max(1, starLeft - textLeft - 8),
        SummaryHeight);

    private void DisposeViews()
    {
        foreach (ClipboardEntryView view in timeline.Items.OfType<ClipboardEntryView>()) view.Dispose();
        timeline.Items.Clear();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
