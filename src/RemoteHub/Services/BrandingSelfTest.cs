using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace RemoteHub.Services;

internal static class BrandingSelfTest
{
    public static void Run(MainWindow window, Action<bool, string> check)
    {
        var assembly = typeof(App).Assembly;
        check(assembly.GetName().Name == "RemoteMachine" &&
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product == "RemoteMachine" &&
            assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title == "RemoteMachine" &&
            window.Title.StartsWith("RemoteMachine", StringComparison.Ordinal),
            "Assembly, product metadata, and native window title identify RemoteMachine");
        check(((TextBlock)window.FindName("TitleBrandName")).Text == "RemoteMachine" &&
            ((TextBlock)window.FindName("BrandVersion")).Text == "RemoteMachine  0.3.6" &&
            System.Windows.Automation.AutomationProperties.GetName((Button)window.FindName("BrandHome")) == "RemoteMachine Home",
            "Titlebar, footer, and accessible home action use the exact RemoteMachine name");
        CheckShellName(window, check);
        check(new WorkspaceStore().DirectoryPath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteWorkspace") &&
            new AppSettingsStore().FilePath == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteWorkspace", "settings.json"),
            "The rename retains the existing connection and appearance storage locations");
        var title = (Image)window.FindName("TitleLogo");
        var sidebar = (Image)window.FindName("SidebarLogo");
        check(title.Source is BitmapSource { PixelWidth: 264, PixelHeight: 264 } && ReferenceEquals(title.Source, sidebar.Source),
            "Titlebar and sidebar reuse the supplied R-arrow mark rather than a generic monitor icon");
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/RemoteHub.ico")).Stream;
        var decoder = new IconBitmapDecoder(resource, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
        check(decoder.Frames.Select(frame => frame.PixelWidth).Order().SequenceEqual(sizes) &&
            decoder.Frames.All(frame => frame.PixelWidth == frame.PixelHeight),
            "The Windows icon includes all ten requested pixel sizes, with square, undistorted artwork");
        var mark = new FormatConvertedBitmap((BitmapSource)title.Source, PixelFormats.Bgra32, null, 0);
        byte[] corner = new byte[4];
        mark.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
        check(corner[3] == 0, "The app symbol has a transparent exterior for light and dark Windows surfaces");
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("The running executable path is unavailable.");
        var metadata = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
        check(Path.GetFileName(exe) == "RemoteMachine.exe" && metadata.ProductName == "RemoteMachine" &&
            metadata.FileDescription == "RemoteMachine" && metadata.FileVersion == "0.3.6.0" &&
            ExtractIconEx(exe, -1, IntPtr.Zero, IntPtr.Zero, 0) > 0,
            "RemoteMachine.exe exposes the renamed product, version, and native Windows icon");
        using var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exe)
            ?? throw new InvalidDataException("Windows could not extract the executable icon.");
        using var actual = extracted.ToBitmap();
        resource.Position = 0;
        using var expectedIcon = new System.Drawing.Icon(resource, actual.Width, actual.Height);
        using var expected = expectedIcon.ToBitmap();
        bool equal = actual.Size == expected.Size;
        for (int y = 0; equal && y < actual.Height; y++)
            for (int x = 0; equal && x < actual.Width; x++)
                equal = actual.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb();
        check(equal, "The icon extracted from the actual EXE matches the supplied logo-derived ICO pixel for pixel");
        check(window.Icon is not null &&
            SendMessage(new WindowInteropHelper(window).Handle, 0x007F, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero,
            "The running HWND exposes its branded large icon to Windows taskbar and Alt+Tab");
        actual.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "remotemachine-exe-icon.png"), System.Drawing.Imaging.ImageFormat.Png);
    }
    internal static void CheckShellName(MainWindow window, Action<bool, string> check)
    {
        window.UpdateLayout();
        var name = (TextBlock)window.FindName("SidebarBrandName");
        var home = (Button)window.FindName("BrandHome");
        var text = new FormattedText(name.Text, System.Globalization.CultureInfo.CurrentUICulture, name.FlowDirection,
            new Typeface(name.FontFamily, name.FontStyle, name.FontWeight, name.FontStretch), name.FontSize, name.Foreground,
            VisualTreeHelper.GetDpi(name).PixelsPerDip);
        var bounds = name.TransformToAncestor(home).TransformBounds(new Rect(name.RenderSize));
        check(name.Text == "RemoteMachine" && name.IsVisible && text.WidthIncludingTrailingWhitespace <= name.ActualWidth + 1 &&
            bounds.Left >= 0 && bounds.Right <= home.ActualWidth - home.Padding.Right + 1,
            $"The complete RemoteMachine name fits without clipping (window {window.ActualWidth:0}, text {text.WidthIncludingTrailingWhitespace:0.0}/{name.ActualWidth:0.0}, right {bounds.Right:0.0}/{home.ActualWidth - home.Padding.Right:0.0})");
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr large, IntPtr small, uint count);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
