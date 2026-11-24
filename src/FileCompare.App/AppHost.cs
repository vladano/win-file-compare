using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using FileCompare.Core.Settings;
using FileCompare.Core.Shell;
using FileCompare.Core.Text;

namespace FileCompare.App;

/// <summary>
/// Startup and the commands that arrive from Explorer or from later launches.
/// One copy runs per user session; later launches hand their arguments to it (see <see cref="SingleInstanceChannel"/>).
/// </summary>
public sealed class AppHost : IDisposable
{
    private static readonly string ChannelName = SingleInstanceChannel.NameFor(AppDataPaths.InstanceName);

    /// <summary>How long to wait for more "Compare selected items" launches (Explorer starts one per item).</summary>
    private static readonly TimeSpan SelectionPause = TimeSpan.FromMilliseconds(700);

    private readonly Application _app;
    private readonly LeftSideStore _leftSide = new();
    private readonly PendingSelection _selection = new();
    private readonly DispatcherTimer _selectionTimer;
    private SingleInstanceChannel? _channel;

    /// <summary>
    /// A fixed name the installer checks (Inno Setup "AppMutex") to ask the user to close File Compare
    /// before files are replaced or removed. Must match AppMutex in installer\FileCompare.iss.
    /// </summary>
    public const string InstallerMutexName = "FileCompare.Running";
    private Mutex? _installerMutex;

    public AppHost(Application app)
    {
        _app = app;
        _selectionTimer = new DispatcherTimer { Interval = SelectionPause };
        _selectionTimer.Tick += (_, _) => FinishSelection();
    }

    /// <summary>Handles the launch. Returns false when the app should exit right away (the work was handed over).</summary>
    public bool Start(string[] rawArgs)
    {
        string[] args = WithFullPaths(rawArgs);
        var parsed = CommandLineArgs.Parse(args);

        if (parsed.Action is LaunchAction.RegisterShell or LaunchAction.UnregisterShell)
        {
            RunShellRegistration(parsed.Action == LaunchAction.RegisterShell);
            return false;
        }

        _channel = new SingleInstanceChannel(ChannelName);
        if (_channel.TryBecomePrimary())
        {
            // Every running copy holds it, also one with its own data folder (the installer tests rely on that).
            _installerMutex = new Mutex(initiallyOwned: false, InstallerMutexName);
            _channel.StartListening(received =>
                _app.Dispatcher.BeginInvoke(() => Handle(CommandLineArgs.Parse(received), fromOtherCopy: true)));
        }
        else
        {
            _channel.Dispose();
            _channel = null;

            // Let the running copy bring its window to the front (Windows only allows that when we pass it on).
            AllowSetForegroundWindow(AsfwAny);
            if (SingleInstanceChannel.TrySend(ChannelName, args, TimeSpan.FromSeconds(5))) return false;
            // Nobody answered (the other copy is closing or stuck): carry on as a normal, separate copy.
        }

        Handle(parsed, fromOtherCopy: false);
        return true;
    }

    // ----- Commands -----

    private void Handle(CommandLineArgs args, bool fromOtherCopy)
    {
        switch (args.Action)
        {
            case LaunchAction.SelectLeft when args.Left is not null:
                SelectLeft(args.Left);
                break;

            case LaunchAction.CompareToLeft when args.Left is not null:
                CompareToLeft(args.Left);
                break;

            case LaunchAction.CompareSelected when args.Left is not null:
                _selection.Add(args.Left);
                _selectionTimer.Stop();
                _selectionTimer.Start(); // waits for the other items of the same Explorer click
                break;

            default:
                Open(new CommandLineArgs(args.Left, args.Right));
                break;
        }

        ExitIfIdle();
    }

    private void SelectLeft(string path)
    {
        _leftSide.Set(path);
        UpdateCompareToMenu(path);
    }

    private void CompareToLeft(string right)
    {
        string? left = _leftSide.Get();
        _leftSide.Clear();
        UpdateCompareToMenu(null);

        // Without a remembered left side (it was deleted meanwhile), open with the clicked item and let the user pick.
        Open(left is null ? new CommandLineArgs(right, null) : new CommandLineArgs(left, right));
    }

    private void FinishSelection()
    {
        _selectionTimer.Stop();
        var paths = _selection.Take();

        if (paths.Count == 1)
        {
            // One item is not a comparison: remember it as the left side, like "Select left side".
            SelectLeft(paths[0]);
        }
        else if (paths.Count >= 2)
        {
            var window = Open(new CommandLineArgs(paths[0], paths[1]));
            if (paths.Count > 2)
            {
                MessageBox.Show(window, $"{paths.Count} items were selected. Only two can be compared, so the first two are shown.",
                    "File Compare", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        ExitIfIdle();
    }

    /// <summary>Opens the paths as a new tab in the window used last (no paths: the home tab).</summary>
    private static Window Open(CommandLineArgs args) => ShellWindow.Show(PageLauncher.Create(args));

    /// <summary>
    /// A copy started only to remember a left side has nothing to show, so it must not stay running unseen.
    /// Windows close the app as usual when the last one closes.
    /// </summary>
    private void ExitIfIdle()
    {
        if (_app.Windows.Count == 0 && _selection.Count == 0 && !_selectionTimer.IsEnabled)
            _app.Shutdown();
    }

    // ----- Explorer menu -----

    private static ShellRegistration Registration => ShellRegistration.ForCurrentUser(Environment.ProcessPath ?? "FileCompare.exe");

    /// <summary>Shows or removes "Compare to 'name'" in the Explorer menu (only when the menu is installed).</summary>
    private static void UpdateCompareToMenu(string? leftSide)
    {
        try
        {
            var registration = Registration;
            // Use the path the menu was installed with, so a copy started from elsewhere does not redirect the menu.
            var installed = registration.InstalledExePath;
            if (installed is not null) registration = ShellRegistration.ForCurrentUser(installed);
            registration.SetCompareTo(leftSide);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // The menu text is a convenience; the selection itself is saved either way.
        }
    }

    /// <summary>--register-shell / --unregister-shell, for an installer. Reports problems with a message box.</summary>
    private static void RunShellRegistration(bool install)
    {
        try
        {
            if (install) Registration.Install(new LeftSideStore().Get());
            else
            {
                Registration.Uninstall();
                new LeftSideStore().Clear();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            MessageBox.Show($"The Explorer menu could not be {(install ? "added" : "removed")}:\n\n{ex.Message}",
                "File Compare", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ----- Helpers -----

    /// <summary>
    /// Makes relative paths absolute here, because the running copy that receives them has a different current folder.
    /// Switches and blank arguments are passed on unchanged.
    /// </summary>
    private static string[] WithFullPaths(string[] args) => args.Select(arg =>
    {
        if (string.IsNullOrWhiteSpace(arg) || arg.StartsWith("--", StringComparison.Ordinal)) return arg;
        string path = TextComparer.NormalizePath(arg);
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }).ToArray();

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    public void Dispose()
    {
        _installerMutex?.Dispose();
        _installerMutex = null;
        _selectionTimer.Stop();
        _channel?.Dispose();
        _channel = null;
    }
}
