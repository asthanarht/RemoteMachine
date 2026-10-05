using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using RemoteHub.Models;
using RemoteHub.Native;
using RemoteHub.Views;

namespace RemoteHub.Services;

internal static class SettingsShellSelfTest
{
    public static async Task Run(MainWindow window, Action<bool, string> check)
    {
        var originalPreference = App.Theme.Preference;
        double width = window.Width, height = window.Height;
        var popup = (Popup)window.FindName("AccountPopup");
        try
        {
            CheckPreferenceStorage(check);
            window.Activate();
            var account = (Button)window.FindName("AccountButton");
            account.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            check(popup.IsOpen && popup.PlacementTarget == account &&
                ((TextBlock)window.FindName("WindowsAccountName")).Text == window.CurrentWindowsAccount.QualifiedName,
                "The top-right account button opens the current Windows identity without fetching credentials");
            check(account.TransformToAncestor(window).Transform(new Point()).Y < 39 &&
                account.TransformToAncestor(window).Transform(new Point()).X > window.ActualWidth / 2,
                "The account entry is in the top-right titlebar, not the bottom sidebar");
            CheckAccountAlignment(popup, account, check);
            NativeSelfTest.Snapshot(window, "settings-account-menu");
            var settingsButton = (Button)window.FindName("AccountSettingsButton");
            settingsButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settingsButton), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            check(!popup.IsOpen && account.IsKeyboardFocused, "Escape dismisses the account menu and returns keyboard focus to its button");
            account.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)window.FindName("AccountSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var slot = (ContentControl)window.FindName("PageContent");
            check(!popup.IsOpen && slot.Content is SettingsView, "The account menu opens Settings and closes cleanly");
            var settings = (SettingsView)slot.Content;
            check(((TextBlock)settings.FindName("AboutTitle")).Text == "About RemoteMachine" &&
                ((TextBlock)settings.FindName("AboutVersion")).Text == "Version 0.3.6" &&
                ReferenceEquals(((Image)settings.FindName("AboutLogo")).Source, ((Image)window.FindName("SidebarLogo")).Source) &&
                !Application.Current.Resources.Contains("AppLockup"),
                "About uses the RemoteMachine name and preserved symbol, not the previous name baked into the old wordmark");
            var choice = (ComboBox)settings.FindName("ThemeChoice");
            foreach (var preference in new[] { ThemePreference.Light, ThemePreference.Dark, ThemePreference.System })
            {
                choice.SelectedValue = preference;
                await Task.Delay(80);
                check(App.Theme.Preference == preference && ReferenceEquals(slot.Content, settings) &&
                    (SystemParameters.HighContrast || preference == ThemePreference.System || App.Theme.Current.IsDark == (preference == ThemePreference.Dark)),
                    $"{preference}: the Settings control changes the theme immediately without replacing its page");
                if (preference != ThemePreference.System) NativeSelfTest.Snapshot(window, $"settings-{preference.ToString().ToLowerInvariant()}");
                var applied = App.Theme.Current;
                SendMessage(new WindowInteropHelper(window).Handle, 0x001A, IntPtr.Zero, IntPtr.Zero);
                for (int attempt = 0; attempt < 50 && ReferenceEquals(App.Theme.Current, applied); attempt++) await Task.Delay(20);
                check(!ReferenceEquals(App.Theme.Current, applied) && App.Theme.Preference == preference && App.Theme.Current.Name == applied.Name,
                    $"{preference}: Windows appearance notifications honor the selected preference");
            }
            check(!App.Theme.SavesPreferences && ((TextBox)settings.FindName("SettingsPath")).Text.EndsWith("settings.json", StringComparison.Ordinal),
                "Native checks expose the settings-file location without changing real user preferences");
            window.WindowState = WindowState.Maximized; await Task.Delay(150);
            CheckWorkArea(window, check);
            NativeSelfTest.Snapshot(window, "settings-maximized");
            account.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
            CheckAccountAlignment(popup, account, check);
            popup.IsOpen = false;
            window.WindowState = WindowState.Normal;
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            await Task.Delay(100);
            BrandingSelfTest.CheckShellName(window, check);
            var help = (Button)window.FindName("HelpNav");
            var status = (Border)window.FindName("StatusChrome");
            check(help.TransformToAncestor(window).Transform(new Point(0, help.ActualHeight)).Y <=
                status.TransformToAncestor(window).Transform(new Point()).Y && account.IsVisible,
                "Compact windows keep Settings, help, and the top account entry above the footer");
            NativeSelfTest.Snapshot(window, "settings-compact");
            account.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(80);
            CheckAccountAlignment(popup, account, check);
            popup.IsOpen = false;
            App.Theme.SetPreference(ThemePreference.Light);
            window.ShowHome(); window.ShowSettings();
            check((ThemePreference)((ComboBox)((SettingsView)slot.Content).FindName("ThemeChoice")).SelectedValue == ThemePreference.Light,
                "Settings preserves the chosen theme when navigating away and returning");
        }
        finally
        {
            popup.IsOpen = false;
            window.WindowState = WindowState.Normal;
            window.Width = width; window.Height = height;
            App.Theme.SetPreference(originalPreference);
            window.ShowHome();
        }
    }
    private static void CheckAccountAlignment(Popup popup, Button account, Action<bool, string> check)
    {
        var card = (FrameworkElement)popup.Child;
        var cardRight = card.PointToScreen(new Point(card.ActualWidth, 0));
        var buttonRight = account.PointToScreen(new Point(account.ActualWidth, account.ActualHeight));
        check(popup.IsOpen && Math.Abs(cardRight.X - buttonRight.X) <= 2 && cardRight.Y >= buttonRight.Y,
            "The account menu anchors beneath its top-right button at the current window size");
    }
    private static void CheckPreferenceStorage(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "remote-settings-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppSettingsStore(directory);
            store.Save(new AppSettings());
            using var theme = new SystemTheme(Application.Current, store.Load(), store);
            var palette = theme.Current;
            using (var locked = File.Open(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try { theme.SetPreference(ThemePreference.Light); }
                catch (IOException) { failed = true; }
                check(failed && theme.Preference == ThemePreference.System && ReferenceEquals(palette, theme.Current) && store.Load().Theme == ThemePreference.System,
                    "A failed theme save preserves both the active preference and the saved file");
            }
            theme.SetPreference(ThemePreference.Light);
            check(theme.Preference == ThemePreference.Light && new AppSettingsStore(directory).Load().Theme == ThemePreference.Light,
                "A successful appearance change persists through the actual theme service");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    internal static void CheckWorkArea(MainWindow window, Action<bool, string> check)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var work = NativeMethods.MonitorWorkArea(hwnd);
        if (!NativeMethods.GetWindowRect(hwnd, out var bounds)) throw new Win32Exception("Cannot read maximized test bounds.");
        var status = (Border)window.FindName("StatusChrome");
        var bottom = status.PointToScreen(new Point(status.ActualWidth, status.ActualHeight));
        check(bounds.Left >= work.Left - 1 && bounds.Top >= work.Top - 1 && bounds.Right <= work.Right + 1 && bounds.Bottom <= work.Bottom + 1,
            $"Maximized native bounds fit the taskbar work area ({bounds.Left},{bounds.Top}..{bounds.Right},{bounds.Bottom}; work {work.Left},{work.Top}..{work.Right},{work.Bottom})");
        check(bottom.Y <= work.Bottom + 1 && bottom.X <= work.Right + 1 && status.ActualHeight >= 28,
            "The entire maximized status bar remains visible above the taskbar");
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
