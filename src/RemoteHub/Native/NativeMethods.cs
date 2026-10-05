using System.Runtime.InteropServices;

namespace RemoteHub.Native;

internal static class NativeMethods
{
    internal const int WmHotkey = 0x0312;
    internal const int WmDisplayChange = 0x007e;
    internal const int GwlExStyle = -20;
    internal const int WsExToolWindow = 0x80;
    internal const int WsExNoActivate = 0x08000000;
    internal const uint SwpNoActivate = 0x10;
    internal const uint SwpNoZOrder = 0x4;
    internal const uint SwpFrameChanged = 0x20;
    internal const uint MonitorDefaultToNearest = 2;
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X; internal int Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        internal int Left, Top, Right, Bottom;
        internal int Width => Right - Left;
        internal int Height => Bottom - Top;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo
    {
        internal uint Size; internal Rect Monitor; internal Rect Work; internal uint Flags;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
    [DllImport("user32.dll")] internal static extern IntPtr GetCapture();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);

    internal static Rect MonitorBounds(IntPtr hwnd)
        => GetMonitor(hwnd).Monitor;
    internal static Rect MonitorWorkArea(IntPtr hwnd)
        => GetMonitor(hwnd).Work;
    private static MonitorInfo GetMonitor(IntPtr hwnd)
    {
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The monitor bounds could not be read.");
        return info;
    }
    internal static void Position(IntPtr hwnd, int x, int y, int width, int height, bool activate = false)
    {
        if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | (activate ? 0 : SwpNoActivate)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The native window could not be positioned.");
    }
}
