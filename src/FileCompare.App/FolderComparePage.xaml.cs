using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FileCompare.App.Folders;
using FileCompare.App.Tabs;
using FileCompare.App.Theming;
using FileCompare.Core.Folders;
using FileCompare.Core.Sessions;
using FileCompare.Core.Text;
using Microsoft.Win32;

namespace FileCompare.App;

/// <summary>
/// Folder compare tab. This file holds scanning, the rows, navigation and drops.
/// Copy, delete, update and opening files are in FolderComparePage.Actions.cs.
/// </summary>
public partial class FolderComparePage : PathComparePage
{
    private readonly BulkObservableCollection<FolderRow> _rows = new();
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private FolderCompareSession? _session;
    private CancellationTokenSource? _cancel;
    private bool _busy;

    /// <summary>The file contents are being compared in the background (the grid can be used meanwhile).</summary>
    private bool _comparing;
    private Task? _contentTask;

    /// <summary>The user opened or closed a folder, so the folders are not opened for them when the compare ends.</summary>
    private bool _userToggled;

    /// <summary>How often content results are put into the grid.</summary>
    private static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(200);

    public FolderComparePage(string? left, string? right)
    {
        InitializeComponent();
        Header = "Folder compare";
        Title = "Folder Compare - File Compare";
        FolderGrid.ItemsSource = _rows;

        SetPaths(left, right);
        InitializePathBar(ThemeMenu, ThemeButtonMenu);
    }

    protected override TextBox LeftPathBox => LeftPath;
    protected override TextBox RightPathBox => RightPath;

    // Two files belong in a file compare.
    protected override bool ShowsKind(CompareKind kind) => kind == CompareKind.Folder;

    protected override string? PickPath(TextBox target)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder" };
        string current = TextComparer.NormalizePath(target.Text);
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        return ShowDialog(dialog) ? dialog.FolderName : null;
    }

    public override CompareTarget? Target =>
        _session is null ? null : new CompareTarget(CompareKind.Folder, _session.LeftRoot, _session.RightRoot);

    protected override void OnFirstLoaded() => Run(ReloadAsync);

    /// <summary>A scan or copy that is still running is stopped (after asking) when the tab closes.</summary>
    public override bool CanClose()
    {
        if (!_busy) return true;
        if (Ask("An operation is still running. Stop it and close the tab?", "Folder Compare",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;
        _cancel?.Cancel();
        return true;
    }

    public override void OnClosed()
    {
        _cancel?.Cancel();
        base.OnClosed();
        if (_busy) Mouse.OverrideCursor = null;
    }

    private bool Ready => _session is not null && !_busy && !_comparing;

    /// <summary>Copy, delete and update wait until the contents are compared; says so if they must wait.</summary>
    private bool CanChangeFiles()
    {
        if (_comparing) StatusText.Text = "Still comparing the contents. Wait until it is done, or press Esc to stop.";
        return Ready;
    }

    private FolderFilter CurrentFilter =>
        (ShowDifferentButton.IsChecked == true ? FolderFilter.Different : 0) |
        (ShowLeftOnlyButton.IsChecked == true ? FolderFilter.LeftOnly : 0) |
        (ShowRightOnlyButton.IsChecked == true ? FolderFilter.RightOnly : 0) |
        (ShowSameButton.IsChecked == true ? FolderFilter.Same : 0);

    private string LeftRoot => _session?.LeftRoot ?? "";
    private string RightRoot => _session?.RightRoot ?? "";

    // ----- Scanning -----

    /// <summary>Scans both folders again from the path boxes.</summary>
    protected override async Task<bool> ReloadAsync()
    {
        if (_busy) return true;
        await StopComparingAsync(); // a new scan replaces a content compare that is still running
        if (_busy) return true;

        string left = LeftPathText;
        string right = RightPathText;
        if (left.Length == 0 || right.Length == 0)
        {
            ShowMessage(left.Length == 0 && right.Length == 0 ? "Pick two folders to compare, or drop them here" : "Pick the other folder to compare");
            return true;
        }
        if (File.Exists(left) && File.Exists(right))
        {
            // Two files belong in a file compare; this tab keeps what it shows.
            OpenInNewTab(left, right);
            StatusText.Text = "Opened the two files in a new tab";
            return true;
        }
        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            ShowMessage(!Directory.Exists(left) ? $"Folder not found: {left}" : $"Folder not found: {right}");
            return true;
        }

        _cancel = new CancellationTokenSource();
        var cancel = _cancel;
        SetBusy(true);
        var progress = new Progress<int>(count => StatusText.Text = $"Listing... {count:N0} items (Esc to cancel)");
        try
        {
            // Phase 1: the listing. Sizes, names (and times) are known; same-size files are still "Comparing...".
            var session = await FolderCompareSession.ScanAsync(left, right, UserSettings.Current.Folders.ToOptions(), cancel.Token, progress);
            _session = session;

            _userToggled = false;
            SetDefaultExpanded(session);
            RebuildRows(keepScroll: false);

            SetTitles(left, right, "Folder Compare");
            UpdateSummary();
            RecordInHistory();

            // Phase 2: the contents, in the background.
            if (session.Contents is null) ShowDone(session, stopped: false);
            else _contentTask = CompareContentsAsync(session, cancel);
        }
        catch (OperationCanceledException)
        {
            ShowMessage("Cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
        return true;
    }

    private void SetDefaultExpanded(FolderCompareSession session)
    {
        _expanded.Clear();
        foreach (var path in FolderListBuilder.DefaultExpanded(session.Root)) _expanded.Add(path);
    }

    /// <summary>
    /// Reads the files of the same size in the background. Every <see cref="BatchInterval"/> the results found so far
    /// go into the grid, so thousands of results do not redraw it thousands of times.
    /// </summary>
    private async Task CompareContentsAsync(FolderCompareSession session, CancellationTokenSource cancel)
    {
        _comparing = true;
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = BatchInterval };
        timer.Tick += (_, _) => ApplyBatch(session);
        ShowContentProgress(session);
        timer.Start();

        bool stopped = false;
        string? error = null;
        try
        {
            await session.CompareContentsAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            stopped = true;
            error = ex.Message;
        }
        finally
        {
            timer.Stop();
            _comparing = false;
        }

        if (!ReferenceEquals(_session, session)) return; // the tab was closed or rescanned

        ShowChanges(stopped ? session.MarkContentsStopped() : session.ApplyContentResults());
        if (!stopped && !_userToggled)
        {
            // Now it is known which folders differ: open them, as after a scan in one go.
            SetDefaultExpanded(session);
            RebuildRows();
        }
        UpdateSummary();
        ShowDone(session, stopped, error);
    }

    private void ApplyBatch(FolderCompareSession session)
    {
        if (!ReferenceEquals(_session, session)) return;
        var changed = session.ApplyContentResults();
        if (changed.Count > 0)
        {
            ShowChanges(changed);
            UpdateSummary();
        }
        ShowContentProgress(session);
    }

    /// <summary>
    /// Shows new statuses. With every kind of entry shown, the rows are updated in place (no redraw, no jump).
    /// With a filter, rows may have to appear or disappear, so the list is built again.
    /// </summary>
    private void ShowChanges(IReadOnlyList<FolderNode> changed)
    {
        if (changed.Count == 0) return;
        if (CurrentFilter != FolderFilter.All) RebuildRows();
        else foreach (var row in _rows) row.RefreshStatus();
    }

    private void ShowContentProgress(FolderCompareSession session)
    {
        if (session.Contents is not { } contents) return;
        StatusText.Text = $"Comparing contents... {contents.Done:N0} of {contents.Total:N0} files, " +
                          $"{FolderFormat.Size(contents.BytesRead)} of {FolderFormat.Size(contents.BytesTotal)} (Esc to stop)";
    }

    private void ShowDone(FolderCompareSession session, bool stopped, string? error = null)
    {
        int notCompared = session.Summary.Pending;
        StatusText.Text = error is not null ? $"Error: {error}"
            : stopped ? $"Stopped: {notCompared:N0} file(s) were not compared. Press F5 to compare again."
            : "Done";

        TimeText.Text = session.Contents is null
            ? $"Scan time: {session.Elapsed.TotalSeconds:0.00} seconds"
            : $"Scan time: {session.Elapsed.TotalSeconds:0.00} seconds (list {session.FirstResultsTime.TotalSeconds:0.00} s, " +
              $"{session.Parallelism} file(s) at a time)";
    }

    /// <summary>Stops a content compare that is still running and waits until it has put what it found into the tree.</summary>
    private async Task StopComparingAsync()
    {
        if (_contentTask is not { } task) return;
        if (!task.IsCompleted) _cancel?.Cancel();
        await task;
        _contentTask = null;
    }

    /// <summary>Shows a message when there is nothing to show, and clears the grid.</summary>
    private void ShowMessage(string message)
    {
        _session = null;
        _rows.ReplaceAll(Array.Empty<FolderRow>());
        StatusText.Text = message;
        SummaryText.Text = "";
        TimeText.Text = "";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
        FolderGrid.IsEnabled = !busy;
        if (!busy && IsLoaded) FolderGrid.Focus();
    }

    private void UpdateSummary()
    {
        if (_session is null) return;
        var s = _session.Summary;
        SummaryText.Text = $"{s.Total:N0} files: {s.Same:N0} same, {s.Different:N0} different, " +
                           $"{s.LeftOnly:N0} left only, {s.RightOnly:N0} right only" +
                           (s.Errors > 0 ? $", {s.Errors:N0} errors" : "") +
                           (s.Pending > 0 ? (_comparing ? $", {s.Pending:N0} being compared" : $", {s.Pending:N0} not compared") : "");
    }

    // ----- Rows -----

    /// <summary>Builds the grid rows from the tree with the current filter and open folders.</summary>
    private void RebuildRows(bool keepScroll = true)
    {
        if (_session is null)
        {
            _rows.ReplaceAll(Array.Empty<FolderRow>());
            return;
        }

        double offset = keepScroll ? FindScrollViewer(FolderGrid)?.VerticalOffset ?? 0 : 0;
        var selected = new HashSet<string>(SelectedRows().Select(r => r.RelativePath), StringComparer.OrdinalIgnoreCase);

        var visible = FolderListBuilder.Build(_session.Root, CurrentFilter, _expanded);
        _rows.ReplaceAll(visible.Select(v => new FolderRow(v)));

        FolderGrid.SelectedItems.Clear();
        foreach (var row in _rows.Where(r => selected.Contains(r.RelativePath))) FolderGrid.SelectedItems.Add(row);

        if (keepScroll)
        {
            Dispatcher.BeginInvoke(() => FindScrollViewer(FolderGrid)?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }
    }

    private List<FolderRow> SelectedRows() => FolderGrid.SelectedItems.OfType<FolderRow>().ToList();

    private List<FolderNode> SelectedNodes() => SelectedRows().Select(r => r.Node).ToList();

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>Opens or closes one folder row by adding or removing the rows below it (the grid keeps its scroll position).</summary>
    private void ToggleRow(FolderRow row)
    {
        if (!row.HasChildren || _session is null) return;
        _userToggled = true;
        int index = _rows.IndexOf(row);
        if (index < 0) return;

        if (row.IsExpanded)
        {
            _expanded.Remove(row.RelativePath);
            row.IsExpanded = false;
            while (index + 1 < _rows.Count && _rows[index + 1].Depth > row.Depth) _rows.RemoveAt(index + 1);
        }
        else
        {
            _expanded.Add(row.RelativePath);
            row.IsExpanded = true;
            var inside = FolderListBuilder.BuildChildren(row.Node, row.Depth + 1, CurrentFilter, _expanded);
            foreach (var visible in inside) _rows.Insert(++index, new FolderRow(visible));
        }
    }

    private void OnExpanderClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderRow row })
        {
            ToggleRow(row);
            e.Handled = true;
        }
    }

    private void OnExpandAll(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;

        void Walk(FolderNode node)
        {
            if (!node.IsFolder) return;
            if (node.RelativePath.Length > 0) _expanded.Add(node.RelativePath);
            foreach (var child in node.Children) Walk(child);
        }

        _userToggled = true;
        Walk(_session.Root);
        RebuildRows(keepScroll: false);
    }

    private void OnCollapseAll(object sender, RoutedEventArgs e)
    {
        _userToggled = true;
        _expanded.Clear();
        RebuildRows(keepScroll: false);
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) RebuildRows(keepScroll: false);
    }

    protected override void OnThemeChanged()
    {
        foreach (var row in _rows) row.RefreshBrush();
    }

    // ----- Input -----

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only react to double clicks on a row, not on the header or the scroll bar.
        if (ItemsControl.ContainerFromElement(FolderGrid, e.OriginalSource as DependencyObject) is DataGridRow)
            OpenSelected();
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        var row = FolderGrid.SelectedItem as FolderRow;
        switch (e.Key)
        {
            case Key.Enter:
                OpenSelected();
                break;
            case Key.Right when row is { HasChildren: true, IsExpanded: false }:
                ToggleRow(row);
                break;
            case Key.Left when row is { HasChildren: true, IsExpanded: true }:
                ToggleRow(row);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { Run(ReloadAsync); e.Handled = true; }
        else if (e.Key == Key.Escape && (_busy || _comparing)) { _cancel?.Cancel(); e.Handled = true; }
    }

}
