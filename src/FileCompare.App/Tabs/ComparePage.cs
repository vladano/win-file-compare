using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FileCompare.App.Dialogs;
using FileCompare.Core.Sessions;

namespace FileCompare.App.Tabs;

/// <summary>
/// One tab of the main window: a text, folder, hex or image compare, or the home page.
/// Holds what all pages share: titles, history, the Home and Sessions buttons and the common menu commands.
/// The XAML of a page can use the protected handlers below by name.
/// </summary>
public abstract class ComparePage : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ComparePage), new PropertyMetadata("File Compare"));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(ComparePage), new PropertyMetadata("New tab"));

    private bool _started;

    protected ComparePage()
    {
        SetResourceReference(BackgroundProperty, "App.Background");
        SetResourceReference(ForegroundProperty, "App.Text");

        // A tab's content is loaded each time the tab is selected, so the start-up work runs only the first time.
        Loaded += (_, _) =>
        {
            if (_started) return;
            _started = true;
            OnFirstLoaded();
        };
    }

    /// <summary>The window title while this tab is selected.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        protected set => SetValue(TitleProperty, value);
    }

    /// <summary>The short text on the tab.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        protected set => SetValue(HeaderProperty, value);
    }

    /// <summary>What the page compares, for the recent list and sessions. Null while nothing is open.</summary>
    public abstract CompareTarget? Target { get; }

    /// <summary>Runs once, the first time the page is shown.</summary>
    protected virtual void OnFirstLoaded() { }

    /// <summary>Asks about unsaved changes or running work. False keeps the tab open.</summary>
    public virtual bool CanClose() => true;

    /// <summary>The tab was closed: stop timers and unsubscribe from shared events.</summary>
    public virtual void OnClosed() { }

    /// <summary>The window the page is in (for dialogs and message boxes).</summary>
    protected Window? OwnerWindow => Window.GetWindow(this) ?? Application.Current.MainWindow;

    /// <summary>The tabbed window the page is in.</summary>
    protected ShellWindow? Host => Window.GetWindow(this) as ShellWindow ?? ShellWindow.Current;

    /// <summary>Sets the tab text and the window title for two paths.</summary>
    protected void SetTitles(string left, string right, string view, bool modified = false)
    {
        string mark = modified ? " *" : "";
        Header = History.SuggestName(left, right) + mark;
        Title = $"{History.ShortName(left)} <--> {History.ShortName(right)}{mark} - {view}";
    }

    /// <summary>Opens paths in a new tab, in the view that suits them.</summary>
    protected void OpenInNewTab(string left, string right) =>
        ShellWindow.Show(PageLauncher.Create(new CommandLineArgs(left, right)), Host);

    /// <summary>Adds the current comparison to the recent list.</summary>
    protected void RecordInHistory()
    {
        if (Target is { } target) AppHistory.Record(target);
    }

    protected MessageBoxResult Ask(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = OwnerWindow;
        return owner is null
            ? MessageBox.Show(text, caption, buttons, image)
            : MessageBox.Show(owner, text, caption, buttons, image);
    }

    /// <summary>
    /// Starts async work without waiting for it. Unlike "_ = work()", an unexpected error is not lost:
    /// it reaches the app's error handler, which shows it.
    /// </summary>
    protected static async void Run(Func<Task> work) => await work();

    /// <summary>Shows an open-file or open-folder dialog over the window the page is in.</summary>
    protected bool ShowDialog(Microsoft.Win32.CommonDialog dialog) =>
        (OwnerWindow is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    protected static string KindName(CompareKind kind) => kind switch
    {
        CompareKind.Folder => "folder compare",
        CompareKind.Hex => "hex compare",
        CompareKind.Image => "image compare",
        _ => "text compare"
    };

    protected static void OpenMenu(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ----- Handlers shared by every page (referenced from XAML) -----

    protected void OnHomeClick(object sender, RoutedEventArgs e) => ShellWindow.ShowHome(Host);

    protected void OnNewTabClick(object sender, RoutedEventArgs e) => ShellWindow.ShowHome(Host, newTab: true);

    protected void OnCloseTabClick(object sender, RoutedEventArgs e) => Host?.CloseTab(this);

    protected void OnExitClick(object sender, RoutedEventArgs e) => OwnerWindow?.Close();

    protected void OnAbout(object sender, RoutedEventArgs e)
    {
        if (OwnerWindow is { } owner) AboutWindow.ShowFor(owner);
    }

    protected void OnShellIntegration(object sender, RoutedEventArgs e)
    {
        if (OwnerWindow is { } owner) ShellIntegrationDialog.ShowFor(owner);
    }

    protected void OnThemeButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) OpenMenu(menu, button);
    }

    /// <summary>The Sessions toolbar button: save this comparison, open a saved session or a recent comparison.</summary>
    protected void OnSessionsButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var menu = button.ContextMenu ??= new ContextMenu();
        menu.Items.Clear();

        var save = new MenuItem { Header = "Save as session...", IsEnabled = Target is not null };
        save.Click += OnSaveSessionClick;
        menu.Items.Add(save);

        menu.Items.Add(new Separator());
        menu.Items.Add(Caption("Saved sessions"));
        if (AppHistory.Sessions.Count == 0) menu.Items.Add(Caption("(none yet)", bold: false));
        foreach (var session in AppHistory.Sessions)
            menu.Items.Add(OpenItem(session.Name, session.Left, session.Right));

        menu.Items.Add(new Separator());
        AddRecentItems(menu.Items);
        OpenMenu(menu, button);
    }

    /// <summary>File > Recent: filled each time it opens.</summary>
    protected void OnRecentMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || !ReferenceEquals(e.OriginalSource, menu)) return;
        menu.Items.Clear();
        AddRecentItems(menu.Items, withCaption: false);
    }

    protected void OnSaveSessionClick(object sender, RoutedEventArgs e)
    {
        if (Target is not { } target) return;

        string? name = InputDialog.Ask(OwnerWindow, "Save session", "Name of the session:",
            History.SuggestName(target.Left, target.Right));
        if (string.IsNullOrWhiteSpace(name)) return;

        if (History.HasSession(AppHistory.Sessions, name) &&
            Ask($"A session called \"{name.Trim()}\" exists already. Replace it?", "Save session",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        AppHistory.SaveSession(name, target);
    }

    private void AddRecentItems(ItemCollection items, bool withCaption = true)
    {
        if (withCaption) items.Add(Caption("Recent"));
        var recent = AppHistory.Recent.Take(10).ToList();
        if (recent.Count == 0) items.Add(Caption("(none yet)", bold: false));
        foreach (var entry in recent)
            items.Add(OpenItem(History.SuggestName(entry.Left, entry.Right), entry.Left, entry.Right));
    }

    private MenuItem OpenItem(string text, string left, string right)
    {
        // A TextBlock header keeps "_" in names from turning into keyboard shortcuts.
        var item = new MenuItem { Header = new TextBlock { Text = text }, ToolTip = $"{left}\n{right}" };
        item.Click += (_, _) => OpenInNewTab(left, right);
        return item;
    }

    private static MenuItem Caption(string text, bool bold = true) => new()
    {
        Header = new TextBlock { Text = text },
        IsEnabled = false,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
    };
}
