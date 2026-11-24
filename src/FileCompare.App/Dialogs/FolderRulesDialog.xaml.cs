using System.Windows;
using FileCompare.App.Theming;
using FileCompare.Core.Folders;
using FileCompare.Core.Settings;

namespace FileCompare.App.Dialogs;

/// <summary>Folder compare rules: which names to skip, and how files are compared.</summary>
public partial class FolderRulesDialog : Window
{
    public sealed record ModeItem(string Label, FolderCompareMode Value);

    private static readonly ModeItem[] ModeItems =
    {
        new("Content (exact)", FolderCompareMode.Content),
        new("Size and time (fast)", FolderCompareMode.SizeAndTime)
    };

    public sealed record ParallelismItem(string Label, int Value);

    private static readonly ParallelismItem[] ParallelismItems =
    {
        new("Automatic (recommended)", 0),
        new("1 (spinning disks)", 1),
        new("2", 2),
        new("4", 4),
        new("8", 8),
        new("16", 16)
    };

    public FolderRulesDialog(FolderSettings current)
    {
        InitializeComponent();
        ThemeService.Track(this);

        PatternsBox.Text = string.Join(Environment.NewLine, current.IgnorePatterns);
        ModeBox.ItemsSource = ModeItems;
        ModeBox.SelectedValue = current.Mode;
        ParallelismBox.ItemsSource = ParallelismItems;
        ParallelismBox.SelectedValue = ParallelismItems.Any(i => i.Value == current.Parallelism) ? current.Parallelism : 0;
    }

    /// <summary>The settings chosen by the user. Valid after the dialog returned true.</summary>
    public FolderSettings Result { get; private set; } = new();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = new FolderSettings
        {
            IgnorePatterns = PatternsBox.Text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith('#'))
                .ToList(),
            Mode = ModeBox.SelectedValue is FolderCompareMode mode ? mode : FolderCompareMode.Content,
            Parallelism = ParallelismBox.SelectedValue is int parallelism ? parallelism : 0
        };
        DialogResult = true;
    }
}
