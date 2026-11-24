using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using FileCompare.App.Tabs;
using FileCompare.App.Theming;

namespace FileCompare.App;

/// <summary>
/// The main window: a row of tabs, each holding one page (home, text, folder, hex or image compare).
/// New comparisons, including ones started from Explorer, open as tabs in the window used last.
/// </summary>
public partial class ShellWindow : Window
{
    /// <summary>A tab in the strip. Pages are controls, so the strip holds these instead of the pages themselves.</summary>
    public sealed record Tab(ComparePage Page);

    private readonly ObservableCollection<Tab> _tabs = new();

    public ShellWindow()
    {
        InitializeComponent();
        TabStrip.ItemsSource = _tabs;
        ThemeService.Track(this);

        SetBinding(TitleProperty, new Binding("SelectedItem.Page.Title")
        {
            Source = TabStrip,
            FallbackValue = "File Compare",
            TargetNullValue = "File Compare"
        });

        Activated += (_, _) => Current = this;
        Closed += OnWindowClosed;
        Current ??= this;
    }

    /// <summary>The window used last; new tabs go there.</summary>
    public static ShellWindow? Current { get; private set; }

    public IEnumerable<ComparePage> Pages => _tabs.Select(t => t.Page);

    private ComparePage? SelectedPage => (TabStrip.SelectedItem as Tab)?.Page;

    /// <summary>Shows a page in a new tab of <paramref name="host"/> (or of the window used last, or of a new window) and brings it to the front.</summary>
    public static ShellWindow Show(ComparePage page, ShellWindow? host = null)
    {
        var window = host ?? Current ?? new ShellWindow();
        window.Add(page);
        window.BringToFront();
        return window;
    }

    /// <summary>Selects the home tab, or opens one. <paramref name="newTab"/> always opens a new one.</summary>
    public static void ShowHome(ShellWindow? host, bool newTab = false)
    {
        var window = host ?? Current;
        var home = window?._tabs.FirstOrDefault(t => t.Page is HomePage);
        if (window is not null && home is not null && !newTab)
        {
            window.TabStrip.SelectedItem = home;
            window.BringToFront();
            return;
        }
        Show(new HomePage(), window);
    }

    public void Add(ComparePage page)
    {
        var tab = new Tab(page);
        _tabs.Add(tab);
        TabStrip.SelectedItem = tab;
    }

    /// <summary>Puts <paramref name="newPage"/> in the tab of <paramref name="oldPage"/> (the home page opening a comparison).</summary>
    public void Replace(ComparePage oldPage, ComparePage newPage)
    {
        int index = IndexOf(oldPage);
        if (index < 0)
        {
            Add(newPage);
            return;
        }

        var tab = new Tab(newPage);
        _tabs[index] = tab;
        oldPage.OnClosed();
        TabStrip.SelectedItem = tab;
    }

    /// <summary>Closes a tab after the page agreed (unsaved changes). The window closes with its last tab.</summary>
    public bool CloseTab(ComparePage page)
    {
        int index = IndexOf(page);
        if (index < 0) return true;

        TabStrip.SelectedIndex = index; // show it before asking about unsaved changes
        if (!page.CanClose()) return false;

        _tabs.RemoveAt(index);
        page.OnClosed();

        if (_tabs.Count == 0) Close();
        else TabStrip.SelectedIndex = Math.Min(index, _tabs.Count - 1);
        return true;
    }

    private int IndexOf(ComparePage page)
    {
        for (int i = 0; i < _tabs.Count; i++)
            if (ReferenceEquals(_tabs[i].Page, page)) return i;
        return -1;
    }

    private void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // ----- Events -----

    private void OnNewTabClick(object sender, RoutedEventArgs e) => ShowHome(this, newTab: true);

    private void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Tab tab }) CloseTab(tab.Page);
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: Tab tab })
        {
            CloseTab(tab.Page);
            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl) return;

        switch (e.Key)
        {
            case Key.W when SelectedPage is { } page:
                CloseTab(page);
                break;
            case Key.T:
                ShowHome(this, newTab: true);
                break;
            case Key.Tab when _tabs.Count > 1:
            case Key.PageDown when _tabs.Count > 1 && !shift:
            case Key.PageUp when _tabs.Count > 1 && !shift:
                int step = e.Key == Key.PageUp || (e.Key == Key.Tab && shift) ? -1 : 1;
                TabStrip.SelectedIndex = (TabStrip.SelectedIndex + step + _tabs.Count) % _tabs.Count;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Asks every tab about unsaved changes; one "Cancel" keeps the window open.</summary>
    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        foreach (var tab in _tabs.ToList())
        {
            TabStrip.SelectedItem = tab;
            if (!tab.Page.CanClose())
            {
                e.Cancel = true;
                return;
            }
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        foreach (var tab in _tabs) tab.Page.OnClosed();
        _tabs.Clear();
        if (ReferenceEquals(Current, this))
            Current = Application.Current.Windows.OfType<ShellWindow>().FirstOrDefault(w => !ReferenceEquals(w, this));
    }
}
