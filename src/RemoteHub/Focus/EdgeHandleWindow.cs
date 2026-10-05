using System.Windows.Interop;
using RemoteHub.Models;
using RemoteHub.Native;

namespace RemoteHub.Focus;

public sealed class EdgeHandleWindow : Window
{
    private readonly Border _sliver;
    private NativeMethods.Point _start;
    private bool _pressed;
    private bool _dragged;
    private NativeMethods.Rect _bounds;
    private DockEdge _edge;
    private double _position;
    public event EventHandler? Invoked;
    public event Action<DockEdge, double>? Docked;
    public EdgeHandleWindow(Window owner)
    {
        Owner = owner; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Width = 32; Height = 80;
        Title = "RemoteMachine session edge handle"; Icon = owner.Icon;
        _sliver = new Border { Name = "Grip", Width = 10, Height = 48, CornerRadius = new(5) };
        _sliver.SetResourceReference(BackgroundProperty, "AccentFill");
        var rim = new Border { Name = "ContrastRim", Padding = new(2), CornerRadius = new(7), Child = _sliver };
        rim.SetResourceReference(BackgroundProperty, "HandleRim");
        var outline = new Border { Name = "ContrastOutline",
            Padding = new(1), CornerRadius = new(8), Child = rim, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll };
        outline.SetResourceReference(BackgroundProperty, "HandleOutline");
        var hitArea = new Grid { Background = Brushes.Transparent, ToolTip = "Click for session controls. Drag to any edge." };
        hitArea.Children.Add(outline); Content = hitArea;
        System.Windows.Automation.AutomationProperties.SetName(hitArea, "Session controls. Drag this handle to any edge.");
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle,
                (IntPtr)(NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64() | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow));
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!NativeMethods.GetCursorPos(out _start)) return;
            _pressed = true; _dragged = false; CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, _) =>
        {
            if (!_pressed || !NativeMethods.GetCursorPos(out var point)) return;
            if (!_dragged && Math.Abs(point.X - _start.X) + Math.Abs(point.Y - _start.Y) < 7) return;
            _dragged = true;
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.GetWindowRect(hwnd, out var rect);
            NativeMethods.Position(hwnd, Math.Clamp(point.X - rect.Width / 2, _bounds.Left, _bounds.Right - rect.Width),
                Math.Clamp(point.Y - rect.Height / 2, _bounds.Top, _bounds.Bottom - rect.Height), rect.Width, rect.Height);
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_pressed) return;
            _pressed = false; ReleaseMouseCapture(); e.Handled = true;
            if (_dragged && NativeMethods.GetCursorPos(out var point))
            {
                var edge = NearestEdge(point.X, point.Y, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height);
                double position = edge is DockEdge.Top or DockEdge.Bottom ? (point.X - _bounds.Left) / (double)_bounds.Width : (point.Y - _bounds.Top) / (double)_bounds.Height;
                Docked?.Invoke(edge, Math.Clamp(position, 0, 1));
            }
            else Invoked?.Invoke(this, EventArgs.Empty);
        };
        LostMouseCapture += (_, _) => { if (_pressed) { _pressed = false; Docked?.Invoke(_edge, _position); } };
    }
    public static DockEdge NearestEdge(int x, int y, int left, int top, int width, int height)
    {
        var distances = new[] { (DockEdge.Left, Math.Abs(x - left)), (DockEdge.Top, Math.Abs(y - top)),
            (DockEdge.Right, Math.Abs(left + width - x)), (DockEdge.Bottom, Math.Abs(top + height - y)) };
        return distances.OrderBy(item => item.Item2).First().Item1;
    }
    internal void Place(NativeMethods.Rect bounds, DockEdge edge, double position)
    {
        _bounds = bounds; _edge = edge; _position = position;
        bool horizontal = edge is DockEdge.Top or DockEdge.Bottom;
        Width = horizontal ? 80 : 32; Height = horizontal ? 32 : 80;
        _sliver.Width = horizontal ? 48 : 10; _sliver.Height = horizontal ? 10 : 48;
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        double scale = NativeMethods.GetDpiForWindow(new WindowInteropHelper(Owner).Handle) / 96d;
        int width = (int)Math.Round(Width * scale), height = (int)Math.Round(Height * scale);
        int along = (int)Math.Clamp((horizontal ? bounds.Width : bounds.Height) * position, 46 * scale,
            (horizontal ? bounds.Width : bounds.Height) - 46 * scale);
        int x = horizontal ? bounds.Left + along - width / 2 : edge == DockEdge.Left ? bounds.Left : bounds.Right - width;
        int y = horizontal ? edge == DockEdge.Top ? bounds.Top : bounds.Bottom - height : bounds.Top + along - height / 2;
        NativeMethods.Position(hwnd, x, y, width, height);
        UpdateLayout();
    }
}
