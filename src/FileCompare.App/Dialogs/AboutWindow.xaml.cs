using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using FileCompare.App.Theming;

namespace FileCompare.App.Dialogs;

/// <summary>Shows the version, license and project links (source code, README, changelog, releases, issues).</summary>
public partial class AboutWindow : Window
{
    private readonly AppInfo _info;
    private readonly Action<string> _openUrl;

    /// <param name="openUrl">Opens a link; defaults to the user's browser.</param>
    public AboutWindow(AppInfo info, Action<string>? openUrl = null)
    {
        InitializeComponent();
        ThemeService.Track(this);
        _info = info;
        _openUrl = openUrl ?? OpenInBrowser;

        ProductText.Text = info.Product;
        VersionText.Text = "Version " + info.VersionText;
        DescriptionText.Text = AppInfo.Description;
        TaglineText.Text = AppInfo.Tagline;
        CopyrightText.Text = info.Copyright.Length > 0 ? $"{info.Copyright}. {AppInfo.LicenseName}." : AppInfo.LicenseName + ".";
        BuiltWithText.Text = AppInfo.BuiltWith;
        RepositoryUrlText.Text = AppInfo.RepositoryUrl;
        LicenseRun.Text = "Free and open source: " + AppInfo.LicenseName;

        RepositoryLink.NavigateUri = new Uri(AppInfo.RepositoryUrl);
        ReadmeLink.NavigateUri = new Uri(AppInfo.ReadmeUrl);
        ChangelogLink.NavigateUri = new Uri(AppInfo.ChangelogUrl);
        ReleasesLink.NavigateUri = new Uri(AppInfo.ReleasesUrl);
        IssuesLink.NavigateUri = new Uri(AppInfo.IssuesUrl);
        LicenseLink.NavigateUri = new Uri(AppInfo.LicenseUrl);
    }

    /// <summary>Opens the About window over <paramref name="owner"/>.</summary>
    public static void ShowFor(Window owner) => new AboutWindow(AppInfo.Current) { Owner = owner }.ShowDialog();

    /// <summary>Text that "Copy details" puts on the clipboard.</summary>
    public string DetailsText => _info.Details();

    private void OnLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            _openUrl(e.Uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the link:\n{e.Uri.AbsoluteUri}\n\n{ex.Message}",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DetailsText);
            CopyButton.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard busy (another program holds it); the user can simply try again.
        }
    }

    private static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
