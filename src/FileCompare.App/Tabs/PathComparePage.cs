using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileCompare.App.Theming;
using FileCompare.Core.Sessions;
using FileCompare.Core.Text;

namespace FileCompare.App.Tabs;

/// <summary>
/// A page that compares the two paths typed into a left and a right path box: text, folder, hex and image compare.
/// Holds what these pages have in common: the path boxes, Swap, Reload, browsing, dropping files,
/// and the theme menus. A page only says how to (re)load, how to pick a path and which kinds of items it shows.
/// The XAML of a page can use the protected handlers below by name.
/// </summary>
public abstract class PathComparePage : ComparePage
{
    private ThemeMenus? _themeMenus;

    /// <summary>The path box of the left side.</summary>
    protected abstract TextBox LeftPathBox { get; }

    /// <summary>The path box of the right side.</summary>
    protected abstract TextBox RightPathBox { get; }

    /// <summary>The left path, trimmed and cleaned up for use.</summary>
    protected string LeftPathText => TextComparer.NormalizePath(LeftPathBox.Text);

    /// <summary>The right path, trimmed and cleaned up for use.</summary>
    protected string RightPathText => TextComparer.NormalizePath(RightPathBox.Text);

    /// <summary>
    /// Reads the paths in the boxes again and shows the result.
    /// Returns false when the user cancelled (unsaved changes), so the caller can undo a path change.
    /// </summary>
    protected abstract Task<bool> ReloadAsync();

    /// <summary>Shows the dialog to choose a path for <paramref name="target"/>. Null when the user cancelled.</summary>
    protected abstract string? PickPath(TextBox target);

    /// <summary>
    /// False for a kind of comparison that another page shows better. Dropped files of that kind open in a new tab
    /// instead of replacing what this page shows.
    /// </summary>
    protected virtual bool ShowsKind(CompareKind kind) => true;

    /// <summary>The theme changed: redraw whatever does not follow the theme by itself.</summary>
    protected virtual void OnThemeChanged() { }

    /// <summary>
    /// Call once from the constructor, after InitializeComponent: fills the theme menus
    /// and lets files be dropped on the path boxes and on the page.
    /// </summary>
    protected void InitializePathBar(ItemsControl themeMenu, ItemsControl themeButtonMenu)
    {
        _themeMenus = new ThemeMenus(themeMenu, themeButtonMenu);
        ThemeService.Changed += HandleThemeChanged;

        AttachDrop(LeftPathBox, DropSide.Left, preview: true);
        AttachDrop(RightPathBox, DropSide.Right, preview: true);
        AttachDrop(this, DropSide.Any);
    }

    /// <summary>Lets files be dropped on an element of the page. <paramref name="side"/> says which path they replace.</summary>
    protected void AttachDrop(UIElement element, DropSide side, bool preview = false) =>
        FileDrop.Attach(element, paths => OnDropped(paths, side), preview);

    /// <summary>Puts paths in the boxes without comparing them.</summary>
    protected void SetPaths(string? left, string? right)
    {
        LeftPathBox.Text = left ?? "";
        RightPathBox.Text = right ?? "";
    }

    public override void OnClosed()
    {
        ThemeService.Changed -= HandleThemeChanged;
        _themeMenus?.Dispose();
        _themeMenus = null;
    }

    private void HandleThemeChanged(object? sender, EventArgs e) => OnThemeChanged();

    // ----- Handlers shared by these pages (referenced from XAML) -----

    protected void OnReload(object sender, RoutedEventArgs e) => Run(ReloadAsync);

    protected virtual void OnSwap(object sender, RoutedEventArgs e)
    {
        (LeftPathBox.Text, RightPathBox.Text) = (RightPathBox.Text, LeftPathBox.Text);
        Run(ReloadAsync);
    }

    protected void OnPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Run(ReloadAsync);
    }

    protected void OnBrowseLeft(object sender, RoutedEventArgs e) => Run(() => BrowseAsync(LeftPathBox));

    protected void OnBrowseRight(object sender, RoutedEventArgs e) => Run(() => BrowseAsync(RightPathBox));

    private async Task BrowseAsync(TextBox target)
    {
        if (PickPath(target) is not { } path) return;

        string previous = target.Text;
        target.Text = path;
        if (!await ReloadAsync()) target.Text = previous;
    }

    /// <summary>Files dropped on a path box or on the page.</summary>
    private void OnDropped(string[] paths, DropSide side)
    {
        string oldLeft = LeftPathBox.Text, oldRight = RightPathBox.Text;
        var (left, right) = DropPlan.Apply(LeftPathText, RightPathText, paths, side);

        if (left.Length > 0 && right.Length > 0 && !ShowsKind(CompareKindDetector.Detect(left, right)))
        {
            OpenInNewTab(left, right);
            return;
        }

        SetPaths(left, right);
        Run(async () =>
        {
            if (await ReloadAsync()) return;
            SetPaths(oldLeft, oldRight); // cancelled because of unsaved changes
        });
    }
}
