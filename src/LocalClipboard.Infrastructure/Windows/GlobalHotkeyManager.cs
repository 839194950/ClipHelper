using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LocalClipboard.Infrastructure.Settings;

namespace LocalClipboard.Infrastructure.Windows;

public sealed partial class GlobalHotkeyManager : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private static readonly nint HwndMessage = new(-3);

    private bool registered;
    private bool disposed;

    public GlobalHotkeyManager()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Global hotkey registration requires an STA thread.");
        }

        CreateHandle(new CreateParams { Parent = HwndMessage });
    }

    public event EventHandler? HotkeyPressed;

    public void Register(HotkeyModifiers modifiers, Keys key)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Unregister();

        if (!RegisterHotKey(Handle, HotkeyId, (uint)modifiers, (uint)key))
        {
            int error = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"Unable to register the global hotkey. Win32 error: {error}.", new Win32Exception(error));
        }

        registered = true;
    }

    public void Unregister()
    {
        if (!registered) return;

        UnregisterHotKey(Handle, HotkeyId);
        registered = false;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (disposed) return;

        Unregister();
        if (Handle != nint.Zero) DestroyHandle();
        disposed = true;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hwnd, int id);
}
