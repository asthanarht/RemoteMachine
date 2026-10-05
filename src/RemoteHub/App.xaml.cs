using System.Diagnostics;
using System.Text.Json;
using RemoteHub.Services;
using RemoteHub.Models;

namespace RemoteHub;

public partial class App : Application
{
    public static SystemTheme Theme { get; private set; } = null!;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write("unhandled-ui-error", details: new { type = args.Exception.GetType().Name, args.Exception.Message, args.Exception.StackTrace });
            MessageBox.Show("The app encountered an unexpected error and must close.\n\n" + args.Exception.Message + "\n\nDiagnostics: " + AppLog.LogPath,
                "RemoteMachine", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
        try
        {
            bool performanceTest = e.Args.Contains("--vnc-performance-test");
            bool selfTest = e.Args.Contains("--self-test") || e.Args.Contains("--interaction-test") || performanceTest;
            var settingsStore = selfTest || e.Args.Contains("--design-preview") ? null : new AppSettingsStore();
            var settings = settingsStore?.Load() ?? new AppSettings();
            if (settingsStore is not null && !File.Exists(settingsStore.FilePath)) settingsStore.Save(settings);
            Theme = new SystemTheme(this, settings, settingsStore);
            var window = new MainWindow(e.Args.Contains("--design-preview"), selfTest);
            MainWindow = window;
            window.Show();
            if (e.Args.Contains("--focus-preview")) window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => OpenPreviewFocus(window)));
            if (selfTest) window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                int exitCode = performanceTest ? await VncSelfTest.RunPerformance(window) :
                    await NativeSelfTest.Run(window, e.Args.Contains("--interaction-test"));
                Shutdown(exitCode);
            }));
            AppLog.Write("app-start", details: new { product = "RemoteMachine", version = "0.3.6", architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), theme = Theme.Current.Name, themePreference = Theme.Preference.ToString() });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            MessageBox.Show("Settings or saved connections could not be opened. Existing files have not been replaced.\n\n" + error.Message, "RemoteMachine", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Theme?.Dispose();
        base.OnExit(e);
    }
    private static async void OpenPreviewFocus(MainWindow window)
    {
        if (!window.IsDesignPreview) return;
        window.OpenConnection(window.Profiles[0]);
        await Task.Delay(400);
        if (window.ActiveSession is { } session) window.EnterFocus(session);
    }
}
