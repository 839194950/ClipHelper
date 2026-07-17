using System.Diagnostics;
using LocalClipboard.App.UI;
using LocalClipboard.Core.Models;
using LocalClipboard.Core.Services;
using LocalClipboard.Infrastructure.Diagnostics;
using LocalClipboard.Infrastructure.Settings;
using LocalClipboard.Infrastructure.Windows;

namespace LocalClipboard.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppPaths paths;
    private readonly JsonSettingsStore settingsStore;
    private readonly StartupManager startupManager;
    private readonly HistoryService historyService;
    private readonly SingleInstanceCoordinator singleInstance;
    private readonly RollingFileLogger logger;
    private readonly GlobalHotkeyManager hotkeyManager;
    private readonly ClipboardMonitorWindow monitor;
    private readonly ClipboardWriter clipboardWriter;
    private readonly PopupForm popup;
    private readonly ContextMenuStrip trayMenu;
    private readonly ToolStripMenuItem pauseItem;
    private readonly NotifyIcon notifyIcon;
    private readonly CancellationTokenSource lifetime = new();

    private AppSettings currentSettings;
    private bool hotkeyRegistered;
    private bool oversizedImageNotified;
    private int exiting;

    internal TrayApplicationContext(
        AppPaths paths,
        AppSettings settings,
        JsonSettingsStore settingsStore,
        StartupManager startupManager,
        HistoryService historyService,
        SingleInstanceCoordinator singleInstance,
        RollingFileLogger logger,
        bool recoveredCorruption)
    {
        this.paths = paths;
        currentSettings = settings;
        this.settingsStore = settingsStore;
        this.startupManager = startupManager;
        this.historyService = historyService;
        this.singleInstance = singleInstance;
        this.logger = logger;

        monitor = new ClipboardMonitorWindow(CaptureClipboardAsync);
        clipboardWriter = new ClipboardWriter(monitor);
        hotkeyManager = new GlobalHotkeyManager();
        hotkeyManager.HotkeyPressed += (_, _) => ShowPopup();

        popup = new PopupForm(
            paths.ImagesRoot,
            historyService.QueryAsync,
            historyService.DeleteAsync,
            historyService.SetFavoriteAsync,
            ActivateEntryAsync);
        _ = popup.Handle;

        var openItem = new ToolStripMenuItem("打开历史", null, (_, _) => ShowPopup());
        pauseItem = new ToolStripMenuItem("暂停监听", null, (_, _) => TogglePause());
        var settingsItem = new ToolStripMenuItem("设置", null, ShowSettings);
        var clearItem = new ToolStripMenuItem("清空历史", null, ClearHistory);
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => ExitThread());
        trayMenu = new ContextMenuStrip();
        trayMenu.Items.AddRange([openItem, pauseItem, settingsItem, clearItem, new ToolStripSeparator(), exitItem]);

        notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = trayMenu,
            Icon = SystemIcons.Application,
            Text = "Local Clipboard",
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowPopup();

        TryRegisterInitialHotkey();
        if (recoveredCorruption)
        {
            ShowNotification("数据库已恢复", "检测到损坏的历史数据库，原文件已移至 recovery 目录。", ToolTipIcon.Warning);
        }
    }

    internal void StartSecondaryInstanceListener() => _ = ListenForSecondaryInstancesAsync();

    internal void ShowPopup()
    {
        if (popup.IsDisposed || Volatile.Read(ref exiting) != 0) return;
        if (popup.InvokeRequired)
        {
            try
            {
                popup.BeginInvoke(ShowPopup);
            }
            catch (InvalidOperationException) when (popup.IsDisposed)
            {
            }
            return;
        }

        popup.ShowPopup();
    }

    protected override void ExitThreadCore()
    {
        if (Interlocked.Exchange(ref exiting, 1) != 0) return;

        notifyIcon.Visible = false;
        lifetime.Cancel();
        popup.Dispose();
        monitor.Dispose();
        hotkeyManager.Dispose();
        notifyIcon.Dispose();
        trayMenu.Dispose();
        base.ExitThreadCore();
    }

    private async Task CaptureClipboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            ClipboardCapture? capture = await ClipboardReader.ReadAsync(cancellationToken);
            if (capture is null) return;
            if (capture.ContentType == ClipboardContentType.Image &&
                capture.PngBytes?.LongLength > RetentionLimits.Default.MaximumSingleImageBytes)
            {
                if (!oversizedImageNotified)
                {
                    oversizedImageNotified = true;
                    ShowNotification(
                        "图片未保存",
                        "图片超过 20 MB，已跳过本次剪贴板记录。",
                        ToolTipIcon.Warning);
                }
                return;
            }

            await historyService.CaptureAsync(capture, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await LogSafelyAsync("clipboard_capture_failed", exception);
        }
    }

    private async Task ActivateEntryAsync(ClipboardEntry entry)
    {
        try
        {
            clipboardWriter.Write(entry, paths.ImagesRoot);
            await historyService.MarkUsedAsync(entry.Id, DateTimeOffset.UtcNow, CancellationToken.None);
        }
        catch (Exception exception)
        {
            await LogSafelyAsync("clipboard_restore_failed", exception);
            throw;
        }
    }

    private void TogglePause()
    {
        monitor.IsPaused = !monitor.IsPaused;
        pauseItem.Text = monitor.IsPaused ? "恢复监听" : "暂停监听";
        notifyIcon.Icon = monitor.IsPaused ? SystemIcons.Warning : SystemIcons.Application;
    }

    private async void ShowSettings(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new SettingsForm(
                currentSettings,
                CalculateCacheBytes(),
                paths.Root,
                SaveSettingsAsync,
                OpenDataDirectory);
            ShowOwnedDialog(dialog);
        }
        catch (Exception exception)
        {
            await LogSafelyAsync("settings_dialog_failed", exception);
            MessageBox.Show("设置窗口打开失败。", "Local Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<bool> SaveSettingsAsync(AppSettings settings)
    {
        AppSettings previous = currentSettings;
        bool previousHotkeyRegistered = hotkeyRegistered;
        try
        {
            hotkeyManager.Register(settings.HotkeyModifiers, settings.HotkeyKey);
            hotkeyRegistered = true;
        }
        catch (Exception exception)
        {
            RestoreHotkey(previous, previousHotkeyRegistered);
            await LogSafelyAsync("hotkey_registration_failed", exception);
            ShowNotification("快捷键未更改", "新快捷键已被占用，原快捷键保持不变。", ToolTipIcon.Warning);
            return false;
        }

        bool startupChanged = previous.StartWithWindows != settings.StartWithWindows;
        try
        {
            if (startupChanged)
            {
                startupManager.SetEnabled(
                    Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable."),
                    settings.StartWithWindows);
            }

            await settingsStore.SaveAsync(settings, CancellationToken.None);
            currentSettings = settings;
            return true;
        }
        catch (Exception exception)
        {
            RestoreHotkey(previous, previousHotkeyRegistered);
            if (startupChanged)
            {
                try
                {
                    startupManager.SetEnabled(Environment.ProcessPath!, previous.StartWithWindows);
                }
                catch (Exception rollbackException)
                {
                    await LogSafelyAsync("startup_setting_rollback_failed", rollbackException);
                }
            }

            await LogSafelyAsync("settings_save_failed", exception);
            ShowNotification("设置未保存", "设置写入失败，请检查数据目录权限。", ToolTipIcon.Warning);
            throw;
        }
    }

    private async void ClearHistory(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new ClearHistoryDialog();
            if (ShowOwnedDialog(dialog) != DialogResult.OK) return;
            await historyService.ClearAsync(dialog.IncludeFavorites, CancellationToken.None);
        }
        catch (Exception exception)
        {
            await LogSafelyAsync("clear_history_failed", exception);
            MessageBox.Show("清空历史失败。", "Local Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ListenForSecondaryInstancesAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                string message = await singleInstance.WaitForMessageAsync(lifetime.Token);
                if (string.Equals(message, "show", StringComparison.Ordinal)) ShowPopup();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                await LogSafelyAsync("single_instance_listener_failed", exception);
                try
                {
                    await Task.Delay(250, lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void TryRegisterInitialHotkey()
    {
        try
        {
            hotkeyManager.Register(currentSettings.HotkeyModifiers, currentSettings.HotkeyKey);
            hotkeyRegistered = true;
        }
        catch (Exception exception)
        {
            hotkeyRegistered = false;
            _ = LogSafelyAsync("initial_hotkey_registration_failed", exception);
            ShowNotification("快捷键不可用", "当前快捷键已被占用，请从托盘菜单打开设置。", ToolTipIcon.Warning);
        }
    }

    private void RestoreHotkey(AppSettings settings, bool shouldRegister)
    {
        try
        {
            if (shouldRegister)
            {
                hotkeyManager.Register(settings.HotkeyModifiers, settings.HotkeyKey);
                hotkeyRegistered = true;
            }
            else
            {
                hotkeyManager.Unregister();
                hotkeyRegistered = false;
            }
        }
        catch (Exception exception)
        {
            hotkeyRegistered = false;
            _ = LogSafelyAsync("hotkey_restore_failed", exception);
        }
    }

    private void ShowNotification(string title, string message, ToolTipIcon icon)
    {
        if (Volatile.Read(ref exiting) != 0) return;
        if (popup.InvokeRequired)
        {
            try
            {
                popup.BeginInvoke(() => ShowNotification(title, message, icon));
            }
            catch (InvalidOperationException) when (popup.IsDisposed)
            {
            }
            return;
        }

        notifyIcon.ShowBalloonTip(5_000, title, message, icon);
    }

    private DialogResult ShowOwnedDialog(Form dialog) => popup.Visible
        ? dialog.ShowDialog(popup)
        : dialog.ShowDialog();

    private void OpenDataDirectory()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add(paths.Root);
        Process.Start(startInfo);
    }

    private long CalculateCacheBytes()
    {
        try
        {
            return Directory
                .EnumerateFiles(paths.Root, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private async Task LogSafelyAsync(string eventName, Exception exception)
    {
        try
        {
            await logger.WriteAsync(eventName, exception, CancellationToken.None);
        }
        catch
        {
        }
    }
}
