using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileCompare.App.Editor;
using FileCompare.App.Tabs;
using FileCompare.App.Theming;
using FileCompare.Core.Sessions;
using FileCompare.Core.Settings;
using FileCompare.Core.Text;
using Microsoft.Win32;

namespace FileCompare.App;

/// <summary>
/// Text compare tab. This file holds the main flow (open, show, view modes, navigation, drops).
/// Editing, copy and save are in TextComparePage.Editing.cs; the Rules and Format dialogs in TextComparePage.Dialogs.cs.
/// </summary>
public partial class TextComparePage : PathComparePage
{
    private readonly DiffPane _left;
    private readonly DiffPane _right;
    private DiffPane _activePane;

    private TextCompareSession? _session;
    private DiffResult _view = new(Array.Empty<DiffRow>());
    private SectionNavigator _navigator = new(null);
    private ViewMode _viewMode = ViewMode.All;
    private bool _showMinor;
    private bool _updatingToolbar;

    /// <summary>Waits for a short pause in typing, then compares again (see <see cref="CommitEdits"/>).</summary>
    private readonly System.Windows.Threading.DispatcherTimer _commitTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private EncodingChoice _leftEncoding = EncodingChoice.Detect;
    private EncodingChoice _rightEncoding = EncodingChoice.Detect;

    private static RuleSettings Rules => UserSettings.Current.Rules;

    public TextComparePage(CommandLineArgs args)
    {
        InitializeComponent();
        Header = "Text compare";
        Title = "Text Compare - File Compare";

        _left = new DiffPane(LeftEditor, isLeft: true);
        _right = new DiffPane(RightEditor, isLeft: false);
        _activePane = _left;
        _ = new ScrollSynchronizer(_left, _right);

        LeftEditor.TextArea.GotFocus += (_, _) => _activePane = _left;
        RightEditor.TextArea.GotFocus += (_, _) => _activePane = _right;
        _left.Edited += OnPaneEdited;
        _right.Edited += OnPaneEdited;
        _commitTimer.Tick += (_, _) => CommitEdits();
        ApplyFormat();

        SetPaths(args.Left, args.Right);
        InitializePathBar(ThemeMenu, ThemeButtonMenu);
        AttachDrop(LeftEditor.TextArea, DropSide.Left, preview: true);
        AttachDrop(RightEditor.TextArea, DropSide.Right, preview: true);
    }

    protected override TextBox LeftPathBox => LeftPath;
    protected override TextBox RightPathBox => RightPath;

    // Compare() sends folders, pictures and binary files to the tab made for them.
    protected override Task<bool> ReloadAsync() => Task.FromResult(Compare());

    protected override string? PickPath(TextBox target)
    {
        var dialog = new OpenFileDialog { CheckFileExists = true };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public override CompareTarget? Target =>
        _session is null ? null : new CompareTarget(CompareKind.Text, _session.Left.Path, _session.Right.Path);

    protected override void OnFirstLoaded() => Compare();

    public override bool CanClose() => ConfirmDiscardChanges();

    public override void OnClosed()
    {
        _commitTimer.Stop();
        base.OnClosed();
    }

    protected override void OnThemeChanged()
    {
        _left.Refresh();
        _right.Refresh();
    }

    // ----- Opening and showing -----

    /// <summary>Loads both files from disk and shows them. Returns false if the user cancelled.</summary>
    private bool Compare(bool confirmDiscard = true)
    {
        if (confirmDiscard && !ConfirmDiscardChanges()) return false;

        string left = LeftPathText;
        string right = RightPathText;
        _navigator = new SectionNavigator(null);

        // Folders, images and other binary files are shown in the view made for them.
        if (left.Length > 0 && right.Length > 0)
        {
            var kind = CompareKindDetector.Detect(left, right);
            if (kind != CompareKind.Text)
            {
                OpenInNewTab(left, right);
                ShowStatus($"Opened in a new {KindName(kind)} tab");
                return true;
            }
        }

        var state = TextComparer.Validate(left, right);
        if (state != CompareInputState.Ready)
        {
            ShowStatus(MessageFor(state));
            return true;
        }

        try
        {
            _session = TextCompareSession.Open(left, right, Rules.ToDiffOptions(), _leftEncoding, _rightEncoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Error: {ex.Message}");
            return true;
        }

        ShowSession(keepScroll: false);
        RecordInHistory();
        return true;
    }

    /// <summary>Rebuilds both panes from the session using the current view mode.</summary>
    private void ShowSession(bool keepScroll)
    {
        if (_session is null) return;

        // Remember where the user was, so a redraw in the middle of typing does not move the caret or the view.
        double scrollY = keepScroll ? LeftEditor.VerticalOffset : 0;
        double scrollX = keepScroll ? LeftEditor.HorizontalOffset : 0;
        var caretPane = _activePane;
        var caret = keepScroll ? caretPane.GetCaret() : (DiffPane.CaretPlace?)null;

        var rows = DiffViewBuilder.Build(_session.Diff.Rows, _viewMode, Rules.ContextLines, _showMinor);
        _view = new DiffResult(rows);
        _navigator = new SectionNavigator(_view);

        _left.SetFile(_session.Left.Path);
        _right.SetFile(_session.Right.Path);
        _left.InlineOptions = _right.InlineOptions = _session.Options;
        _left.Show(rows);
        _right.Show(rows);
        ApplyEditable();

        if (caret is { } place) caretPane.RestoreCaret(place);
        if (keepScroll)
        {
            // Restoring the caret may scroll, so the view is put back afterwards.
            LeftEditor.ScrollToVerticalOffset(scrollY);
            LeftEditor.ScrollToHorizontalOffset(scrollX);
        }

        UpdateInfo();
    }

    /// <summary>Updates the info bars, titles and status bar. Cheap, so it is safe to call while typing.</summary>
    private void UpdateInfo()
    {
        if (_session is null) return;
        var diff = _session.Diff;
        bool leftModified = _session.Left.IsModified || _left.IsDirty;
        bool rightModified = _session.Right.IsModified || _right.IsDirty;

        LeftInfo.Text = Describe(_session.Left.Info, leftModified);
        RightInfo.Text = Describe(_session.Right.Info, rightModified);
        SetTitles(_session.Left.Path, _session.Right.Path, "File Compare", leftModified || rightModified);

        DiffCountText.Text = $"{diff.DifferenceCount} difference section(s)";
        SameText.Text = diff.AreSame ? "Same" : "Different";
        ModeText.Text = $"View: {_viewMode}{(_showMinor ? " + minor" : "")}";
        LoadTimeText.Text = $"Load time: {_session.LoadTime.TotalSeconds:0.00} seconds";
    }

    private static string Describe(LoadedText t, bool modified) =>
        $"{t.LastWriteTime:dd.MM.yyyy HH:mm:ss}   {t.SizeBytes:N0} bytes   {t.EncodingName}   {t.LineEnding}" +
        (modified ? "   (modified)" : "");

    private static string MessageFor(CompareInputState state) => state switch
    {
        CompareInputState.NeedBothPaths => "Pick two files to compare, or drop them here",
        CompareInputState.NeedOtherPath => "Pick the other file to compare",
        CompareInputState.FolderPaths => "Compare a file with a file, or a folder with a folder",
        _ => ""
    };

    private void ShowStatus(string message)
    {
        DiffCountText.Text = message;
        SameText.Text = "";
        ModeText.Text = "";
        LoadTimeText.Text = "";
    }

    private void ApplyFormat()
    {
        _left.ApplyFormat(UserSettings.Current.Format);
        _right.ApplyFormat(UserSettings.Current.Format);
    }

    // ----- View modes -----

    private void OnViewModeChecked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _updatingToolbar) return;

        _viewMode = sender == ViewDiffsButton ? ViewMode.Diffs
            : sender == ViewSameButton ? ViewMode.Same
            : sender == ViewContextButton ? ViewMode.Context
            : ViewMode.All;

        // Filtered views hide lines, so they cannot be edited. Turn editing off instead of leaving a dead button.
        if (_viewMode != ViewMode.All) SetToolbarState(() => EditToggle.IsChecked = false);
        RefreshView(keepScroll: false);
    }

    private void OnMinorChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _updatingToolbar) return;
        _showMinor = MinorToggle.IsChecked == true;
        RefreshView(keepScroll: true);
    }

    /// <summary>Changes toolbar buttons from code without their Checked/Unchecked handlers reacting.</summary>
    private void SetToolbarState(Action change)
    {
        _updatingToolbar = true;
        try { change(); }
        finally { _updatingToolbar = false; }
    }

    /// <summary>Takes in any typing first, then redraws with the current view settings.</summary>
    private void RefreshView(bool keepScroll)
    {
        SyncFromPanes();
        ShowSession(keepScroll);
    }

    // ----- Navigation -----

    private void GoToSection(int direction)
    {
        if (_navigator.Move(direction) is not int row) return;
        _left.ScrollToRow(row);
        _right.ScrollToRow(row);
        DiffCountText.Text = $"Difference {_navigator.Current + 1} of {_navigator.Count}";
    }

    private void OnNextSection(object sender, RoutedEventArgs e) => GoToSection(+1);
    private void OnPrevSection(object sender, RoutedEventArgs e) => GoToSection(-1);

    protected override void OnSwap(object sender, RoutedEventArgs e)
    {
        SyncFromPanes();
        (LeftPath.Text, RightPath.Text) = (RightPath.Text, LeftPath.Text);
        (_leftEncoding, _rightEncoding) = (_rightEncoding, _leftEncoding);
        if (_session is null) return;

        _session.Swap(); // keeps unsaved edits, no reload from disk
        ShowSession(keepScroll: false);
    }

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // In the path boxes Ctrl+Z and Ctrl+Y keep their normal meaning (undo typing in the box).
        bool inPathBox = Keyboard.FocusedElement is TextBox;

        if (e.Key == Key.S && ctrl) { SaveFiles(left: true, right: true); e.Handled = true; }
        else if (e.Key == Key.F5) { Compare(); e.Handled = true; }
        else if (e.Key == Key.F6) { CommitEdits(); e.Handled = true; }
        else if (e.Key == Key.Z && ctrl && !shift && !inPathBox) { UndoRedo(undo: true); e.Handled = true; }
        else if ((e.Key == Key.Y && ctrl || e.Key == Key.Z && ctrl && shift) && !inPathBox) { UndoRedo(undo: false); e.Handled = true; }
    }
}
