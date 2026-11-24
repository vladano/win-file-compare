using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileCompare.App.Tabs;
using FileCompare.App.Theming;
using FileCompare.Core.Sessions;
using FileCompare.Core.Text;
using Microsoft.Win32;

namespace FileCompare.App;

/// <summary>
/// The start tab: start a new comparison, or open a saved session or a recent comparison.
/// Opening something replaces this tab with the comparison.
/// </summary>
public partial class HomePage : ComparePage
{
    /// <summary>One row in the session or recent list.</summary>
    public sealed record Entry(string Title, string Paths, string Kind, string When, string Left, string Right, string? SessionName)
    {
        public Visibility RemoveVisibility => SessionName is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly ThemeMenus _themeMenus;

    public HomePage()
    {
        InitializeComponent();
        Header = "Home";
        Title = "File Compare";

        _themeMenus = new ThemeMenus(ThemeMenu);
        AppHistory.Changed += OnHistoryChanged;

        FileDrop.Attach(LeftPath, paths => OnDropped(paths, DropSide.Left), preview: true);
        FileDrop.Attach(RightPath, paths => OnDropped(paths, DropSide.Right), preview: true);
        FileDrop.Attach(this, paths => OnDropped(paths, DropSide.Any));

        Refresh();
    }

    public override CompareTarget? Target => null;

    protected override void OnFirstLoaded() => LeftPath.Focus();

    public override void OnClosed()
    {
        AppHistory.Changed -= OnHistoryChanged;
        _themeMenus.Dispose();
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var sessions = AppHistory.Sessions
            .Select(s => new Entry(s.Name, $"{s.Left}   ↔   {s.Right}", KindLabel(s.Kind), "", s.Left, s.Right, s.Name))
            .ToList();
        var recent = AppHistory.Recent
            .Select(r => new Entry(History.SuggestName(r.Left, r.Right), $"{r.Left}   ↔   {r.Right}", KindLabel(r.Kind),
                When(r.LastUsed), r.Left, r.Right, null))
            .ToList();

        SessionsList.ItemsSource = sessions;
        RecentList.ItemsSource = recent;
        NoSessionsText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRecentText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearRecentButton.IsEnabled = recent.Count > 0;
    }

    private static string KindLabel(CompareKind kind) => kind switch
    {
        CompareKind.Folder => "Folders",
        CompareKind.Hex => "Hex",
        CompareKind.Image => "Images",
        _ => "Text"
    };

    private static string When(DateTime time)
    {
        var today = DateTime.Today;
        if (time.Date == today) return "Today " + time.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (time.Date == today.AddDays(-1)) return "Yesterday " + time.ToString("HH:mm", CultureInfo.InvariantCulture);
        return time.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    // ----- Starting a comparison -----

    /// <summary>Replaces this tab with the comparison of the two paths.</summary>
    private void Start(string left, string right)
    {
        left = TextComparer.NormalizePath(left);
        right = TextComparer.NormalizePath(right);
        if (left.Length == 0 || right.Length == 0)
        {
            StatusText.Text = "Pick two files or two folders";
            return;
        }

        var page = PageLauncher.Create(new CommandLineArgs(left, right));
        if (Host is { } host) host.Replace(this, page);
        else ShellWindow.Show(page);
    }

    private void OnCompareClick(object sender, RoutedEventArgs e) => Start(LeftPath.Text, RightPath.Text);

    private void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Start(LeftPath.Text, RightPath.Text);
    }

    private void OnOpenEntry(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Entry entry }) Start(entry.Left, entry.Right);
    }

    private void OnRemoveSession(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Entry { SessionName: { } name } }) return;
        if (Ask($"Remove the saved session \"{name}\"? The files are not touched.", "File Compare",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            AppHistory.RemoveSession(name);
    }

    private void OnClearRecent(object sender, RoutedEventArgs e)
    {
        if (Ask("Clear the list of recent comparisons? The files are not touched.", "File Compare",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            AppHistory.ClearRecent();
    }

    /// <summary>Two items start the comparison at once; one item fills a path box.</summary>
    private void OnDropped(string[] paths, DropSide side)
    {
        var (left, right) = DropPlan.Apply(TextComparer.NormalizePath(LeftPath.Text), TextComparer.NormalizePath(RightPath.Text), paths, side);
        LeftPath.Text = left;
        RightPath.Text = right;
        if (paths.Length >= 2) Start(left, right);
        else StatusText.Text = left.Length > 0 && right.Length > 0 ? "Press Compare to start" : "Now pick or drop the other side";
    }

    // ----- Browsing -----

    private void OnBrowseLeftFile(object sender, RoutedEventArgs e) => BrowseFile(LeftPath);
    private void OnBrowseRightFile(object sender, RoutedEventArgs e) => BrowseFile(RightPath);
    private void OnBrowseLeftFolder(object sender, RoutedEventArgs e) => BrowseFolder(LeftPath);
    private void OnBrowseRightFolder(object sender, RoutedEventArgs e) => BrowseFolder(RightPath);

    private void BrowseFile(TextBox target)
    {
        var dialog = new OpenFileDialog { CheckFileExists = true };
        if (ShowDialog(dialog)) target.Text = dialog.FileName;
    }

    private void BrowseFolder(TextBox target)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder" };
        if (ShowDialog(dialog)) target.Text = dialog.FolderName;
    }
}
