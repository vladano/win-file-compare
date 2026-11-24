using System.Windows;
using FileCompare.App.Theming;
using FileCompare.Core.Settings;
using FileCompare.Core.Text;

namespace FileCompare.App.Dialogs;

/// <summary>Rules: which kinds of differences are unimportant, and how much context to show.</summary>
public partial class RulesDialog : Window
{
    public sealed record AlgorithmItem(string Label, DiffAlgorithm Value);

    private static readonly AlgorithmItem[] AlgorithmItems =
    {
        new("Standard (Myers)", DiffAlgorithm.Myers),
        new("Patience", DiffAlgorithm.Patience)
    };

    public RulesDialog(RuleSettings current)
    {
        InitializeComponent();
        ThemeService.Track(this);

        LeadingBox.IsChecked = current.IgnoreLeadingWhitespace;
        EmbeddedBox.IsChecked = current.IgnoreEmbeddedWhitespace;
        TrailingBox.IsChecked = current.IgnoreTrailingWhitespace;
        CaseBox.IsChecked = current.IgnoreCase;

        AlgorithmBox.ItemsSource = AlgorithmItems;
        AlgorithmBox.SelectedValue = current.Algorithm;

        ContextBox.ItemsSource = Enumerable.Range(0, 11).ToArray();
        ContextBox.SelectedItem = Math.Clamp(current.ContextLines, 0, 10);
    }

    /// <summary>The settings chosen by the user. Valid after the dialog returned true.</summary>
    public RuleSettings Result { get; private set; } = new();

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = new RuleSettings
        {
            IgnoreLeadingWhitespace = LeadingBox.IsChecked == true,
            IgnoreEmbeddedWhitespace = EmbeddedBox.IsChecked == true,
            IgnoreTrailingWhitespace = TrailingBox.IsChecked == true,
            IgnoreCase = CaseBox.IsChecked == true,
            Algorithm = AlgorithmBox.SelectedValue is DiffAlgorithm algorithm ? algorithm : DiffAlgorithm.Myers,
            ContextLines = ContextBox.SelectedItem is int lines ? lines : 3
        };
        DialogResult = true;
    }
}
