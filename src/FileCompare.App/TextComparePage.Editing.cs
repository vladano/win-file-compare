using System.IO;
using System.Text;
using System.Windows;
using FileCompare.Core.Text;

namespace FileCompare.App;

// Edit mode, copying differences between the sides, and saving.
public partial class TextComparePage
{
    private void OnEditChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _updatingToolbar) return;
        bool editing = EditToggle.IsChecked == true;

        if (editing && _viewMode != ViewMode.All)
        {
            // Editing needs every line on screen.
            _viewMode = ViewMode.All;
            SetToolbarState(() => ViewAllButton.IsChecked = true);
            RefreshView(keepScroll: false);
        }
        else if (!editing)
        {
            RefreshView(keepScroll: true); // take in the typing and update the highlights
        }
        ApplyEditable();
    }

    private void ApplyEditable()
    {
        bool editable = _session is not null && EditToggle.IsChecked == true && _viewMode == ViewMode.All;
        _left.SetEditable(editable);
        _right.SetEditable(editable);
    }

    /// <summary>Copies what the user typed in the panes into the session (one undo step) and compares again. Does not redraw.</summary>
    /// <returns>True if anything was typed.</returns>
    private bool SyncFromPanes()
    {
        if (_session is null || !(_left.IsDirty || _right.IsDirty)) return false;

        _session.ApplyEdit(
            _left.IsDirty ? _left.GetEditedText() : null,
            _right.IsDirty ? _right.GetEditedText() : null);
        return true;
    }

    private void OnUpdateDifferencesNow(object sender, RoutedEventArgs e) => CommitEdits();

    private void OnPaneEdited(object? sender, EventArgs e)
    {
        UpdateInfo(); // shows "(modified)" right away
        _commitTimer.Stop();
        _commitTimer.Start(); // restarts on every key, so the compare runs after a pause
    }

    /// <summary>
    /// Takes in the typing, compares again and redraws the highlights, keeping the caret and scroll position.
    /// Runs after a pause in typing, and at once on F6, Undo, Save and before anything that needs the new text.
    /// </summary>
    private void CommitEdits()
    {
        _commitTimer.Stop();
        if (SyncFromPanes()) ShowSession(keepScroll: true);
    }

    // ----- Undo and redo -----

    private void OnUndo(object sender, RoutedEventArgs e) => UndoRedo(undo: true);
    private void OnRedo(object sender, RoutedEventArgs e) => UndoRedo(undo: false);

    /// <summary>Steps back or forward through typing and copies. Typing that has not been compared yet becomes its own step first.</summary>
    private void UndoRedo(bool undo)
    {
        if (_session is null) return;
        _commitTimer.Stop();
        SyncFromPanes();

        bool done = undo ? _session.Undo() : _session.Redo();
        if (!done)
        {
            DiffCountText.Text = undo ? "Nothing to undo" : "Nothing to redo";
            return;
        }

        ShowSession(keepScroll: true);
        DiffCountText.Text = undo ? "Undone" : "Redone";
    }

    /// <summary>Greys out Undo and Redo when there is nothing to do.</summary>
    private void OnEditMenuOpened(object sender, RoutedEventArgs e)
    {
        SyncFromPanes(); // typing counts as an undo step even if the pause has not elapsed yet
        UndoItem.IsEnabled = _session?.CanUndo == true;
        RedoItem.IsEnabled = _session?.CanRedo == true;
    }

    // ----- Copy -----

    private void OnCopyButtonClick(object sender, RoutedEventArgs e) => OpenMenu(CopyMenu, CopyButton);
    private void OnCopyToRight(object sender, RoutedEventArgs e) => CopyCurrentSection(CopyDirection.LeftToRight);
    private void OnCopyToLeft(object sender, RoutedEventArgs e) => CopyCurrentSection(CopyDirection.RightToLeft);

    private void CopyCurrentSection(CopyDirection direction)
    {
        if (_session is null) { DiffCountText.Text = "Open two files first"; return; }

        int caretRow = _activePane.CaretRow;
        if (SyncFromPanes()) ShowSession(keepScroll: true);

        if (_view.DifferenceCount != _session.Diff.DifferenceCount)
        {
            DiffCountText.Text = "Switch to All, Diffs or Context to copy differences";
            return;
        }

        int section = FindSectionToCopy(caretRow);
        if (section < 0) { DiffCountText.Text = "No difference to copy"; return; }

        _session.CopySection(section, direction);
        ShowSession(keepScroll: true);
        DiffCountText.Text = direction == CopyDirection.LeftToRight
            ? $"Copied difference {section + 1} to the right"
            : $"Copied difference {section + 1} to the left";
    }

    /// <summary>The section under the caret, else the one last navigated to, else the next one below the caret.</summary>
    private int FindSectionToCopy(int caretRow)
    {
        int section = _view.SectionAt(caretRow);
        if (section >= 0) return section;
        if (_navigator.Current >= 0) return _navigator.Current;
        for (int i = 0; i < _view.SectionStarts.Count; i++)
            if (_view.SectionStarts[i] >= caretRow) return i;
        return _view.DifferenceCount - 1; // caret is below the last section
    }

    // ----- Save -----

    private void OnSaveLeft(object sender, RoutedEventArgs e) => SaveFiles(left: true, right: false);
    private void OnSaveRight(object sender, RoutedEventArgs e) => SaveFiles(left: false, right: true);
    private void OnSaveAll(object sender, RoutedEventArgs e) => SaveFiles(left: true, right: true);

    /// <returns>False if saving failed (the user was told why).</returns>
    private bool SaveFiles(bool left, bool right)
    {
        if (_session is null) return true;
        if (SyncFromPanes()) ShowSession(keepScroll: true);

        try
        {
            if (left && _session.Left.IsModified) _session.Save(left: true);
            if (right && _session.Right.IsModified) _session.Save(left: false);
        }
        catch (EncoderFallbackException)
        {
            ShowError("The text has characters that the file's encoding cannot store.\n\n" +
                      "Change the encoding in Format, or remove those characters, and save again.");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"The file could not be saved:\n\n{ex.Message}");
            return false;
        }

        UpdateInfo();
        DiffCountText.Text = "Saved";
        return true;
    }

    private void ShowError(string message) =>
        Ask(message, "File Compare", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ----- Unsaved changes -----

    /// <summary>Anything typed or copied that is not on disk yet.</summary>
    private bool HasUnsavedChanges()
    {
        SyncFromPanes();
        return _session is not null && _session.IsModified;
    }

    /// <summary>Asks what to do with unsaved changes. Returns false if the user wants to stay.</summary>
    private bool ConfirmDiscardChanges()
    {
        if (!HasUnsavedChanges()) return true;

        var answer = Ask("There are unsaved changes. Save them first?", "File Compare",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => SaveFiles(left: true, right: true),
            MessageBoxResult.No => true,
            _ => false
        };
    }
}
