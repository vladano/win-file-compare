using System.IO;
using System.Windows;
using FileCompare.App.Theming;
using FileCompare.Core.Shell;

namespace FileCompare.App.Dialogs;

/// <summary>Adds or removes the Explorer context menu entries, after showing exactly what is written.</summary>
public partial class ShellIntegrationDialog : Window
{
    private readonly ShellRegistration _registration;

    public ShellIntegrationDialog()
    {
        InitializeComponent();
        ThemeService.Track(this);

        _registration = ShellRegistration.ForCurrentUser(Environment.ProcessPath ?? "FileCompare.exe");
        KeysText.Text = string.Join(Environment.NewLine, _registration.KeyPaths.Select(k => @"HKEY_CURRENT_USER\" + k));
        ExeText.Text = _registration.ExePath;
        Refresh();
    }

    public static void ShowFor(Window owner) => new ShellIntegrationDialog { Owner = owner }.ShowDialog();

    private void Refresh()
    {
        string? installed = SafeInstalledPath();
        bool isInstalled = installed is not null;

        if (!isInstalled) StatusText.Text = "Not installed.";
        else if (string.Equals(installed, _registration.ExePath, StringComparison.OrdinalIgnoreCase))
            StatusText.Text = "Installed for this copy of File Compare.";
        else
            StatusText.Text = $"Installed, but the entries start a different copy:\n{installed}\nInstall again to use this one.";

        InstallButton.Content = isInstalled ? "Install again" : "Install";
        RemoveButton.IsEnabled = isInstalled;

        bool looksTemporary = _registration.ExePath.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
                              _registration.ExePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
        WarningText.Visibility = looksTemporary ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? SafeInstalledPath()
    {
        try { return _registration.InstalledExePath; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    private void OnInstall(object sender, RoutedEventArgs e) => Run(() => _registration.Install(new LeftSideStore().Get()), "added");

    private void OnRemove(object sender, RoutedEventArgs e) => Run(() =>
    {
        _registration.Uninstall();
        new LeftSideStore().Clear();
    }, "removed");

    private void Run(Action change, string done)
    {
        try
        {
            change();
            Refresh();
            StatusText.Text = $"The Explorer menu entries were {done}. " + StatusText.Text;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            MessageBox.Show(this, $"The Explorer menu entries could not be {done}:\n\n{ex.Message}",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
