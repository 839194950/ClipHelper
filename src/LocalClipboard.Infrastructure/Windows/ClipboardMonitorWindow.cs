using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LocalClipboard.Infrastructure.Windows;

public sealed partial class ClipboardMonitorWindow : NativeWindow, IDisposable, IClipboardSuppressionGate
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly nint HwndMessage = new(-3);

    private readonly Func<CancellationToken, Task> onClipboardChanged;
    private readonly SemaphoreSlim notificationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private int suppressNext;
    private int disposed;

    public ClipboardMonitorWindow(Func<CancellationToken, Task> onClipboardChanged)
    {
        this.onClipboardChanged = onClipboardChanged ?? throw new ArgumentNullException(nameof(onClipboardChanged));
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Clipboard monitoring requires an STA thread.");
        }

        CreateHandle(new CreateParams { Parent = HwndMessage });
        if (!AddClipboardFormatListener(Handle))
        {
            int error = Marshal.GetLastPInvokeError();
            DestroyHandle();
            throw new Win32Exception(error);
        }
    }

    public void SuppressNextNotification() => Interlocked.Exchange(ref suppressNext, 1);

    public void CancelSuppression() => Interlocked.Exchange(ref suppressNext, 0);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmClipboardUpdate && Interlocked.Exchange(ref suppressNext, 0) == 0)
        {
            _ = NotifyChangedAsync();
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        lifetime.Cancel();
        if (Handle != nint.Zero)
        {
            RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }

        lifetime.Dispose();
        notificationGate.Dispose();
    }

    private async Task NotifyChangedAsync()
    {
        try
        {
            await notificationGate.WaitAsync(lifetime.Token);
            try
            {
                await onClipboardChanged(lifetime.Token);
        }
        finally
        {
            try
            {
                notificationGate.Release();
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
            {
            }
        }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
        {
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveClipboardFormatListener(nint hwnd);
}
