using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace RemoteHub.Native;

internal sealed class WindowWorkArea : IDisposable
{
    private readonly Window _window;
    private readonly Func<bool> _isFocus;
    private readonly HwndSource _source;
    private bool _queued, _disposed;
    public WindowWorkArea(Window window, Func<bool> isFocus)
    {
        _window = window; _isFocus = isFocus;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)
            ?? throw new InvalidOperationException("The window must be initialized before tracking its work area.");
        _source.AddHook(WindowMessage);
    }
    internal static (NativeMethods.Point Position, NativeMethods.Point Size) MaximizedMetrics(NativeMethods.Rect monitor, NativeMethods.Rect work)
        => (new() { X = work.Left - monitor.Left, Y = work.Top - monitor.Top }, new() { X = work.Width, Y = work.Height });
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed || _isFocus()) return IntPtr.Zero;
        if (message == 0x0024 && lParam != IntPtr.Zero)
        {
            var work = NativeMethods.MonitorWorkArea(hwnd);
            var metrics = MaximizedMetrics(NativeMethods.MonitorBounds(hwnd), work);
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MaxPosition = metrics.Position; info.MaxSize = metrics.Size;
            info.MinTrackSize.X = Math.Min(info.MinTrackSize.X, work.Width);
            info.MinTrackSize.Y = Math.Min(info.MinTrackSize.Y, work.Height);
            Marshal.StructureToPtr(info, lParam, false);
            handled = true;
        }
        if (message is 0x001A or 0x007E or 0x02E0) QueueBoundsCheck();
        return IntPtr.Zero;
    }
    private void QueueBoundsCheck()
    {
        if (_queued) return;
        _queued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _queued = false;
            if (_disposed || _isFocus() || _window.WindowState != WindowState.Maximized) return;
            var work = NativeMethods.MonitorWorkArea(_source.Handle);
            if (!NativeMethods.GetWindowRect(_source.Handle, out var bounds))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The maximized window bounds could not be read.");
            if (!bounds.Equals(work)) NativeMethods.Position(_source.Handle, work.Left, work.Top, work.Width, work.Height);
        }));
    }
    public void Dispose() { _disposed = true; _source.RemoveHook(WindowMessage); }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativeMethods.Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
}
