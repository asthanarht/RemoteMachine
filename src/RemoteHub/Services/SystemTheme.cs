using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Interop;
using Microsoft.Win32;
using RemoteHub.Themes;
using RemoteHub.Models;

namespace RemoteHub.Services;

public sealed class SystemTheme : IDisposable
{
    private readonly Application _app;
    private readonly AppSettingsStore? _store;
    private readonly ResourceDictionary _resources = new();
    private HwndSource? _source;
    private bool _queued, _disposed;
    public ThemePalette Current { get; private set; } = ThemePalette.Create(false, ThemePalette.Parse("#245CE5"));
    public ThemePreference Preference { get; private set; }
    public string SettingsPath => _store?.FilePath ?? new AppSettingsStore().FilePath;
    public bool SavesPreferences => _store is not null;
    public event EventHandler? Changed;

    public SystemTheme(Application app, AppSettings settings, AppSettingsStore? store)
    {
        _app = app; _store = store; Preference = settings.Theme;
        app.Resources.MergedDictionaries.Add(_resources);
        Refresh();
        SystemParameters.StaticPropertyChanged += SystemParametersChanged;
    }
    public void Attach(Window window)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)
            ?? throw new InvalidOperationException("The theme requires an initialized window.");
        _source.AddHook(WindowMessage);
        ApplyWindowFrame();
    }
    public void Refresh()
    {
        _app.Dispatcher.VerifyAccess();
        bool dark = Current.IsDark;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { AppLog.Write("system-theme-read-failed", details: new { type = error.GetType().Name, retainedTheme = Current.Name }); }
        Color accent = Current.Colors["AccentFill"];
        int result = DwmGetColorizationColor(out uint color, out _);
        if (result >= 0) accent = Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);
        else AppLog.Write("system-accent-read-failed", details: new { hresult = result });
        ContrastColors? contrast = SystemParameters.HighContrast
            ? new(SystemColors.WindowColor, SystemColors.WindowTextColor, SystemColors.HighlightColor, SystemColors.HighlightTextColor)
            : null;
        Apply(ThemePalette.Create(UsesDarkPalette(Preference, dark), accent, contrast));
    }
    public void SetPreference(ThemePreference preference)
    {
        _app.Dispatcher.VerifyAccess();
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        if (Preference == preference) return;
        _store?.Save(new AppSettings { Theme = preference });
        Preference = preference;
        Refresh();
    }
    internal static bool UsesDarkPalette(ThemePreference preference, bool systemDark) => preference switch
    {
        ThemePreference.System => systemDark,
        ThemePreference.Light => false,
        ThemePreference.Dark => true,
        _ => throw new ArgumentOutOfRangeException(nameof(preference))
    };
    internal void Apply(ThemePalette palette)
    {
        _app.Dispatcher.VerifyAccess();
        Current = palette;
        foreach (var (key, color) in palette.Colors)
        {
            if (_resources[key] is SolidColorBrush existing && existing.Color == color) continue;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            _resources[key] = brush;
        }
        _resources[SystemColors.WindowBrushKey] = _resources["Surface"];
        _resources[SystemColors.WindowTextBrushKey] = _resources["Ink"];
        _resources[SystemColors.ControlBrushKey] = _resources["Subtle"];
        _resources[SystemColors.ControlTextBrushKey] = _resources["Ink"];
        _resources[SystemColors.HighlightBrushKey] = _resources["AccentFill"];
        _resources[SystemColors.HighlightTextBrushKey] = _resources["OnAccent"];
        _resources[SystemColors.GrayTextBrushKey] = _resources["Muted"];
        _resources[SystemColors.InactiveSelectionHighlightBrushKey] = _resources["AccentSoft"];
        _resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = _resources["Ink"];
        _resources["ThemeDescription"] = palette.IsHighContrast
            ? $"High contrast is active. Your {Preference} preference will resume when Windows contrast themes are off."
            : Preference == ThemePreference.System ? $"Following Windows: {palette.Name}. Accent color follows Windows."
            : $"{palette.Name} theme selected. Accent color follows Windows.";
        ApplyWindowFrame();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void ApplyWindowFrame()
    {
        if (_source is null) return;
        int dark = Current.IsDark ? 1 : 0;
        int result = DwmSetWindowAttribute(_source.Handle, 20, ref dark, sizeof(int));
        if (result < 0) AppLog.Write("system-theme-frame-unavailable", details: new { hresult = result });
    }
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x001A or 0x031A or 0x0320) QueueRefresh(); // Settings, theme, and DWM accent broadcasts.
        return IntPtr.Zero;
    }
    private void SystemParametersChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
    private void QueueRefresh()
    {
        if (!_app.Dispatcher.CheckAccess()) { _app.Dispatcher.BeginInvoke(new Action(QueueRefresh)); return; }
        if (_disposed || _queued) return;
        _queued = true;
        _app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _queued = false;
            if (!_disposed) Refresh();
        }));
    }
    public void Dispose()
    {
        _disposed = true;
        SystemParameters.StaticPropertyChanged -= SystemParametersChanged;
        _source?.RemoveHook(WindowMessage);
        _app.Resources.MergedDictionaries.Remove(_resources);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmGetColorizationColor(out uint color, [MarshalAs(UnmanagedType.Bool)] out bool opaque);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
