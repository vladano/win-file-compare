using System.Windows;
using FileCompare.App.Theming;

namespace FileCompare.App;

public partial class App : Application
{
    private AppHost? _host;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnexpectedError;
        UserSettings.Load();
        ThemeService.Initialize();

        _host = new AppHost(this);
        if (!_host.Start(e.Args)) Shutdown();
    }

    /// <summary>
    /// A bug in one tab should not close the whole app, which would lose unsaved edits in the other tabs.
    /// The error is shown (so it can be reported) and the app keeps running.
    /// </summary>
    private void OnUnexpectedError(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
                        "File Compare keeps running. If this happens again, please report it (About > Report a problem).",
            "File Compare", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
