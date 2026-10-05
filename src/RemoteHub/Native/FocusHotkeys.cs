using System.Runtime.InteropServices;
using System.Windows.Interop;
using RemoteHub.Services;

namespace RemoteHub.Native;

public sealed class FocusHotkeys : IDisposable
{
    private const int ControlsId = 0x52D1;
    private const int ExitId = 0x52D2;
    private readonly HwndSource _source;
    private bool _controlsRegistered;
    private bool _exitRegistered;
    public bool Available => _controlsRegistered && _exitRegistered;
    public event EventHandler? ControlsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? DisplayChanged;
    public FocusHotkeys(Window owner)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(owner).Handle)
            ?? throw new InvalidOperationException("The app window has not been created.");
        _source.AddHook(WindowMessage);
    }
    public bool Register()
    {
        if (Available) return true;
        Unregister();
        const uint modifiers = 0x1 | 0x2 | 0x4000;
        _controlsRegistered = NativeMethods.RegisterHotKey(_source.Handle, ControlsId, modifiers, 0x20);
        _exitRegistered = NativeMethods.RegisterHotKey(_source.Handle, ExitId, modifiers, 0x24);
        if (!Available)
        {
            AppLog.Write("focus-hotkey-conflict", details: new { controls = _controlsRegistered, exit = _exitRegistered, error = Marshal.GetLastWin32Error() });
            Unregister();
        }
        return Available;
    }
    public void Unregister()
    {
        if (_controlsRegistered) NativeMethods.UnregisterHotKey(_source.Handle, ControlsId);
        if (_exitRegistered) NativeMethods.UnregisterHotKey(_source.Handle, ExitId);
        _controlsRegistered = _exitRegistered = false;
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmDisplayChange) DisplayChanged?.Invoke(this, EventArgs.Empty);
        if (message != NativeMethods.WmHotkey) return IntPtr.Zero;
        if (wParam.ToInt32() == ControlsId) { handled = true; ControlsRequested?.Invoke(this, EventArgs.Empty); }
        if (wParam.ToInt32() == ExitId) { handled = true; ExitRequested?.Invoke(this, EventArgs.Empty); }
        return IntPtr.Zero;
    }
    public void Dispose() { Unregister(); _source.RemoveHook(WindowMessage); }
}
