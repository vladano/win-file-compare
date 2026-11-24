using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using FileCompare.App.Folders;
using FileCompare.App.Theming;
using FileCompare.Core.Folders;

namespace FileCompare.App.Dialogs;

/// <summary>
/// Shows what an Update, Mirror or Sync would do before anything is touched: the actions grouped by kind, with
/// sizes and reasons. Items can be left out, and conflicts get a choice. Nothing happens until Run is pressed.
/// </summary>
public partial class SyncPreviewDialog : Window
{
    private readonly SyncPlan _plan;
    private readonly List<SyncRow> _rows;

    public SyncPreviewDialog(SyncPlan plan, string leftRoot, string rightRoot)
    {
        InitializeComponent();
        ThemeService.Track(this);
        _plan = plan;

        (Title, HeadingText.Text, ExplanationText.Text, RunButton.Content) = Describe(plan);
        RootsText.Text = $"Left:  {leftRoot}\nRight: {rightRoot}";

        _rows = plan.Items.Select(i => new SyncRow(i)).ToList();
        foreach (var row in _rows) row.PropertyChanged += (_, _) => UpdateTotals();

        var view = new ListCollectionView(_rows);
        view.SortDescriptions.Add(new SortDescription(nameof(SyncRow.GroupOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SyncRow.Path), ListSortDirection.Ascending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SyncRow.Group)));
        ItemsList.ItemsSource = view;

        UpdateTotals();
    }

    private static (string Title, string Heading, string Explanation, string Run) Describe(SyncPlan plan) => plan.Kind switch
    {
        SyncKind.UpdateRight => ("Update right", "Update the right side",
            "Copies new files and files that are newer on the left to the right. Nothing is deleted.", "Update"),
        SyncKind.UpdateLeft => ("Update left", "Update the left side",
            "Copies new files and files that are newer on the right to the left. Nothing is deleted.", "Update"),
        SyncKind.MirrorRight => ("Mirror to right", "Make the right side an exact copy of the left",
            "Copies what is missing or different on the right, and deletes what exists only on the right (to the Recycle Bin).", "Mirror"),
        SyncKind.MirrorLeft => ("Mirror to left", "Make the left side an exact copy of the right",
            "Copies what is missing or different on the left, and deletes what exists only on the left (to the Recycle Bin).", "Mirror"),
        _ => ("Sync both ways", "Sync both ways",
            plan.IsFirstSync
                ? "First sync of these folders: files on one side are copied to the other, and a file that differs goes from the newer side. " +
                  "Nothing is deleted. From the next sync on, deletes and changes made on either side are brought across."
                : "Brings the changes made on each side since the last sync to the other side, including deletes (to the Recycle Bin). " +
                  "Files changed on both sides are conflicts: pick which side to keep.", "Sync")
    };

    private void UpdateTotals()
    {
        var parts = new List<string>();
        int copies = _plan.FilesToCopy;
        if (copies > 0) parts.Add($"copy {copies:N0} file(s), {FolderFormat.Size(_plan.BytesToCopy)}");
        if (_plan.FilesToDeleteLeft > 0) parts.Add($"delete {_plan.FilesToDeleteLeft:N0} file(s) on the left");
        if (_plan.FilesToDeleteRight > 0) parts.Add($"delete {_plan.FilesToDeleteRight:N0} file(s) on the right");
        int skipped = _plan.Conflicts.Count(c => c.Effective is null);

        TotalsText.Text = parts.Count == 0 ? "Nothing to do." : char.ToUpperInvariant(parts[0][0]) + string.Join(", ", parts)[1..] + ".";
        if (skipped > 0) TotalsText.Text += $" {skipped:N0} conflict(s) skipped.";
        WarningText.Text = _plan.DeleteWarning ?? "";
        WarningText.Visibility = _plan.DeleteWarning is null ? Visibility.Collapsed : Visibility.Visible;
        RunButton.IsEnabled = !_plan.IsEmpty;
    }

    /// <summary>Space toggles the selected items, so many can be left out at once.</summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || ItemsList.SelectedItems.Count == 0) return;
        var selected = ItemsList.SelectedItems.OfType<SyncRow>().Where(r => r.CanInclude).ToList();
        bool include = !selected.All(r => r.Included);
        foreach (var row in selected) row.Included = include;
        e.Handled = true;
    }

    private void OnRun(object sender, RoutedEventArgs e)
    {
        if (_plan.DeleteWarning is { } warning &&
            MessageBox.Show(this, warning + "\n\nContinue?", Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        DialogResult = true;
    }

    /// <summary>One line of the preview.</summary>
    public sealed class SyncRow : INotifyPropertyChanged
    {
        public sealed record ChoiceOption(string Label, ConflictChoice Value);

        private readonly SyncItem _item;

        public SyncRow(SyncItem item)
        {
            _item = item;
            Choices = item.Choices.Select(c => new ChoiceOption(c switch
            {
                ConflictChoice.KeepLeft => item.Node.Left is null ? "Delete right" : "Keep left",
                ConflictChoice.KeepRight => item.Node.Right is null ? "Delete left" : "Keep right",
                _ => "Skip"
            }, c)).ToList();
        }

        public string Path => _item.RelativePath + (_item.Node.IsFolder ? "\\" : "");
        public string Reason => _item.Reason;

        public string SizeText => _item.Action switch
        {
            SyncAction.CopyToRight or SyncAction.CopyToLeft => FolderFormat.Size(_item.Bytes) +
                                                               (_item.Node.IsFolder ? $" ({_item.Files:N0})" : ""),
            SyncAction.DeleteLeft or SyncAction.DeleteRight when _item.Node.IsFolder => $"{_item.Files:N0} file(s)",
            _ => ""
        };

        public (int Order, string Name) GroupKey => _item.Action switch
        {
            SyncAction.CopyToRight => (0, "Copy to the right"),
            SyncAction.CopyToLeft => (1, "Copy to the left"),
            SyncAction.DeleteRight => (2, "Delete on the right"),
            SyncAction.DeleteLeft => (3, "Delete on the left"),
            _ => (4, "Conflicts")
        };

        public int GroupOrder => GroupKey.Order;
        public string Group => GroupKey.Name;

        public bool Included
        {
            get => _item.Included;
            set
            {
                if (_item.Included == value) return;
                _item.Included = value;
                Raise(nameof(Included));
                Raise(nameof(CanChoose));
            }
        }

        /// <summary>A conflict that cannot be resolved here stays out.</summary>
        public bool CanInclude => _item.Action != SyncAction.Conflict || Choices.Count > 1;

        public IReadOnlyList<ChoiceOption> Choices { get; }
        public Visibility ChoiceVisibility => _item.Action == SyncAction.Conflict ? Visibility.Visible : Visibility.Collapsed;
        public bool CanChoose => Choices.Count > 1 && Included;

        public ConflictChoice Choice
        {
            get => _item.Choice;
            set
            {
                if (_item.Choice == value) return;
                _item.Choice = value;
                Raise(nameof(Choice));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
