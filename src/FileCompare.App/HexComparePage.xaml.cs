using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FileCompare.App.Folders;
using FileCompare.App.Tabs;
using FileCompare.App.Theming;
using FileCompare.Core.Binary;
using FileCompare.Core.Sessions;
using FileCompare.Core.Text;
using Microsoft.Win32;

namespace FileCompare.App;

/// <summary>Hex compare tab: two files byte by byte, for binary files.</summary>
public partial class HexComparePage : PathComparePage
{
    private HexComparison? _comparison;
    private int _currentSection = -1;
    private bool _loading;

    public HexComparePage(string left, string right)
    {
        InitializeComponent();
        Header = "Hex compare";
        Title = "Hex Compare - File Compare";
        SetPaths(left, right);
        InitializePathBar(ThemeMenu, ThemeButtonMenu);
        View.SizeChanged += (_, _) => UpdateScrollBar();
    }

    protected override TextBox LeftPathBox => LeftPath;
    protected override TextBox RightPathBox => RightPath;

    // Folders and pictures have views of their own.
    protected override bool ShowsKind(CompareKind kind) => kind is CompareKind.Hex or CompareKind.Text;

    protected override string? PickPath(TextBox target)
    {
        var dialog = new OpenFileDialog { CheckFileExists = true };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public override CompareTarget? Target =>
        _comparison is null ? null : new CompareTarget(CompareKind.Hex, _comparison.LeftPath, _comparison.RightPath);

    protected override void OnFirstLoaded() => Run(ReloadAsync);

    protected override void OnThemeChanged() => View.InvalidateVisual();

    // ----- Loading -----

    /// <summary>Reads both files in the background (they can be large) and shows the result.</summary>
    protected override async Task<bool> ReloadAsync()
    {
        if (_loading) return true;

        string left = LeftPathText;
        string right = RightPathText;
        if (left.Length == 0 && right.Length == 0)
        {
            ShowStatus("Pick two files to compare, or drop them here");
            return true;
        }

        _loading = true;
        ShowStatus("Reading the files...");
        try
        {
            var sw = Stopwatch.StartNew();
            var comparison = await Task.Run(() => HexComparison.Load(left, right));
            sw.Stop();
            Show(comparison, sw.Elapsed);
            if (left.Length > 0 && right.Length > 0) RecordInHistory();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _comparison = null;
            View.Comparison = null;
            UpdateScrollBar();
            LeftInfo.Text = RightInfo.Text = "";
            ShowStatus($"Error: {ex.Message}");
            return true;
        }
        finally
        {
            _loading = false;
        }
    }

    private void Show(HexComparison comparison, TimeSpan elapsed)
    {
        _comparison = comparison;
        _currentSection = -1;
        View.Comparison = comparison;
        UpdateScrollBar();

        LeftInfo.Text = Describe(comparison.LeftPath, comparison.Left.Length);
        RightInfo.Text = Describe(comparison.RightPath, comparison.Right.Length);
        SetTitles(comparison.LeftPath, comparison.RightPath, "Hex Compare");

        if (comparison.LeftPath.Length == 0 || comparison.RightPath.Length == 0)
            DiffCountText.Text = "Pick the other file to compare";
        else if (comparison.AreSame)
            DiffCountText.Text = "The files are identical";
        else
            DiffCountText.Text = $"{comparison.DifferentBytes:N0} different byte(s) in {comparison.DifferenceCount:N0} section(s)" +
                                 (comparison.Left.Length != comparison.Right.Length ? ", the sizes differ" : "");
        SameText.Text = comparison.AreSame ? "Same" : "Different";
        LoadTimeText.Text = $"Load time: {elapsed.TotalSeconds:0.00} seconds";
    }

    private static string Describe(string path, long size)
    {
        if (path.Length == 0) return "";
        var info = new FileInfo(path);
        return $"{info.LastWriteTime:dd.MM.yyyy HH:mm:ss}   {size:N0} bytes ({FolderFormat.Size(size)})";
    }

    private void ShowStatus(string message)
    {
        DiffCountText.Text = message;
        SameText.Text = "";
        LoadTimeText.Text = "";
    }

    // ----- Scrolling and navigation -----

    private void UpdateScrollBar()
    {
        int rows = _comparison?.RowCount ?? 0;
        int visible = View.VisibleRows;
        VerticalScroll.Minimum = 0;
        VerticalScroll.Maximum = Math.Max(0, rows - visible);
        VerticalScroll.ViewportSize = visible;
        VerticalScroll.LargeChange = Math.Max(1, visible - 1);
        VerticalScroll.Value = Math.Min(VerticalScroll.Value, VerticalScroll.Maximum);
    }

    private void ScrollTo(int row) => VerticalScroll.Value = Math.Clamp(row, 0, (int)VerticalScroll.Maximum);

    private void OnScrollValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        View.FirstRow = (int)Math.Round(e.NewValue);

    private void OnViewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        ScrollTo(View.FirstRow - Math.Sign(e.Delta) * 3);
        e.Handled = true;
    }

    private void GoToSection(int direction)
    {
        if (_comparison is not { DifferenceCount: > 0 } c) return;
        int count = c.DifferenceCount;
        _currentSection = ((_currentSection + direction) % count + count) % count;
        ScrollTo(c.SectionStarts[_currentSection] - 3); // a few rows of context above
        DiffCountText.Text = $"Difference {_currentSection + 1} of {count}";
    }

    private void OnNextSection(object sender, RoutedEventArgs e) => GoToSection(+1);
    private void OnPrevSection(object sender, RoutedEventArgs e) => GoToSection(-1);

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { Run(ReloadAsync); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBox) return; // keys in the path boxes are for typing

        int page = View.VisibleRows - 1;
        int? target = e.Key switch
        {
            Key.Down => View.FirstRow + 1,
            Key.Up => View.FirstRow - 1,
            Key.PageDown => View.FirstRow + page,
            Key.PageUp => View.FirstRow - page,
            Key.Home when Keyboard.Modifiers.HasFlag(ModifierKeys.Control) => 0,
            Key.End when Keyboard.Modifiers.HasFlag(ModifierKeys.Control) => int.MaxValue,
            _ => null
        };
        if (target is not int row) return;
        ScrollTo(row);
        e.Handled = true;
    }

    // ----- Commands -----

    private void OnOpenAsText(object sender, RoutedEventArgs e)
    {
        var page = new TextComparePage(new CommandLineArgs(LeftPathText, RightPathText));
        ShellWindow.Show(page, Host);
    }
}
