using System.ComponentModel;
using System.Runtime.InteropServices;
using LocalClipboard.Infrastructure.Settings;

namespace LocalClipboard.App.UI;

internal sealed class SettingsForm : Form
{
    private readonly Func<AppSettings, Task<bool>> saveSettings;
    private readonly Action openDataDirectory;
    private readonly CheckBox startupCheck = new();
    private readonly TextBox hotkeyBox = new();
    private readonly Label validationLabel = new();
    private readonly Label cacheLabel = new();
    private readonly Button saveButton = new();
    private HotkeyModifiers selectedModifiers;
    private Keys selectedKey;

    internal SettingsForm(
        AppSettings currentSettings,
        long cacheBytes,
        string dataDirectory,
        Func<AppSettings, Task<bool>> saveSettings,
        Action openDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        this.openDataDirectory = openDataDirectory ?? throw new ArgumentNullException(nameof(openDataDirectory));
        selectedModifiers = currentSettings.HotkeyModifiers;
        selectedKey = currentSettings.HotkeyKey;

        Text = "设置";
        ClientSize = new Size(460, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);

        BuildControls(currentSettings, cacheBytes, dataDirectory);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool StartWithWindowsChecked => startupCheck.Checked;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string HotkeyDisplayText => hotkeyBox.Text;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string CacheUsageText => cacheLabel.Text;

    internal static SettingsForm CreateForTest(AppSettings settings, long cacheBytes) => new(
        settings,
        cacheBytes,
        Path.GetTempPath(),
        static _ => Task.FromResult(true),
        static () => { });

    private void BuildControls(AppSettings currentSettings, long cacheBytes, string dataDirectory)
    {
        startupCheck.AutoSize = true;
        startupCheck.Location = new Point(24, 24);
        startupCheck.Text = "随 Windows 启动";
        startupCheck.Checked = currentSettings.StartWithWindows;

        var hotkeyLabel = new Label
        {
            AutoSize = true,
            Location = new Point(24, 64),
            Text = "全局快捷键"
        };
        hotkeyBox.Location = new Point(24, 88);
        hotkeyBox.Size = new Size(412, 27);
        hotkeyBox.ReadOnly = true;
        hotkeyBox.Text = FormatHotkey(selectedModifiers, selectedKey);
        hotkeyBox.KeyDown += HotkeyBox_KeyDown;

        validationLabel.AutoSize = false;
        validationLabel.Location = new Point(24, 118);
        validationLabel.Size = new Size(412, 22);
        validationLabel.ForeColor = Color.Firebrick;
        validationLabel.Visible = false;

        var directoryLabel = new Label
        {
            AutoSize = true,
            Location = new Point(24, 151),
            Text = "数据目录"
        };
        var directoryPath = new Label
        {
            AutoEllipsis = true,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(24, 175),
            Size = new Size(314, 28),
            Text = dataDirectory,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var openButton = new Button
        {
            Location = new Point(346, 174),
            Size = new Size(90, 30),
            Text = "打开数据目录"
        };
        openButton.Click += (_, _) => openDataDirectory();

        var retentionLabel = new Label
        {
            AutoSize = true,
            Location = new Point(24, 224),
            Text = "普通历史：最多 500 条 / 30 天"
        };
        cacheLabel.AutoSize = true;
        cacheLabel.Location = new Point(24, 252);
        cacheLabel.Text = $"当前图片缓存：{FormatBytes(cacheBytes)}";

        saveButton.Location = new Point(260, 310);
        saveButton.Size = new Size(84, 32);
        saveButton.Text = "保存";
        saveButton.Click += SaveButton_Click;
        var cancelButton = new Button
        {
            DialogResult = DialogResult.Cancel,
            Location = new Point(352, 310),
            Size = new Size(84, 32),
            Text = "取消"
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.AddRange([
            startupCheck,
            hotkeyLabel,
            hotkeyBox,
            validationLabel,
            directoryLabel,
            directoryPath,
            openButton,
            retentionLabel,
            cacheLabel,
            saveButton,
            cancelButton]);
    }

    private void HotkeyBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        Keys key = e.KeyCode;
        HotkeyModifiers modifiers = ToHotkeyModifiers(e.Modifiers);
        if (IsKeyDown(Keys.LWin) || IsKeyDown(Keys.RWin)) modifiers |= HotkeyModifiers.Windows;

        if (IsModifierKey(key))
        {
            ShowValidation("请输入一个非修饰键");
            return;
        }
        if (modifiers == HotkeyModifiers.None)
        {
            ShowValidation("快捷键必须包含 Alt、Ctrl、Shift 或 Win");
            return;
        }
        if ((modifiers & HotkeyModifiers.Windows) != 0 && key == Keys.V)
        {
            ShowValidation("Win + V 已由 Windows 使用");
            return;
        }

        selectedModifiers = modifiers;
        selectedKey = key;
        hotkeyBox.Text = FormatHotkey(modifiers, key);
        validationLabel.Visible = false;
    }

    private async void SaveButton_Click(object? sender, EventArgs e)
    {
        saveButton.Enabled = false;
        try
        {
            var settings = new AppSettings(startupCheck.Checked, selectedModifiers, selectedKey);
            if (await saveSettings(settings))
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            ShowValidation("快捷键已被其他程序占用");
        }
        catch (Exception)
        {
            ShowValidation("设置保存失败");
        }
        finally
        {
            if (!IsDisposed) saveButton.Enabled = true;
        }
    }

    private void ShowValidation(string text)
    {
        validationLabel.Text = text;
        validationLabel.Visible = true;
    }

    private static HotkeyModifiers ToHotkeyModifiers(Keys modifiers)
    {
        HotkeyModifiers result = HotkeyModifiers.None;
        if ((modifiers & Keys.Alt) != 0) result |= HotkeyModifiers.Alt;
        if ((modifiers & Keys.Control) != 0) result |= HotkeyModifiers.Control;
        if ((modifiers & Keys.Shift) != 0) result |= HotkeyModifiers.Shift;
        return result;
    }

    private static bool IsModifierKey(Keys key) => key is
        Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin;

    private static bool IsKeyDown(Keys key) => (GetKeyState((int)key) & 0x8000) != 0;

    private static string FormatHotkey(HotkeyModifiers modifiers, Keys key)
    {
        var parts = new List<string>(5);
        if ((modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & HotkeyModifiers.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private static string FormatBytes(long bytes)
    {
        double megabytes = Math.Max(0, bytes) / (1024d * 1024d);
        return $"{megabytes:0.#} MB";
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
