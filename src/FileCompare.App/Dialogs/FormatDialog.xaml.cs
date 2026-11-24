using System.Windows;
using FileCompare.App.Theming;
using FileCompare.Core.Settings;
using FileCompare.Core.Text;

namespace FileCompare.App.Dialogs;

/// <summary>Format: encoding override for each file, tab size and visible whitespace.</summary>
public partial class FormatDialog : Window
{
    public sealed record EncodingItem(string Label, EncodingChoice Value);

    private static readonly EncodingItem[] EncodingItems =
    {
        new("Detect automatically", EncodingChoice.Detect),
        new("UTF-8", EncodingChoice.Utf8),
        new("UTF-8 with BOM", EncodingChoice.Utf8Bom),
        new("UTF-16 LE", EncodingChoice.Utf16Le),
        new("UTF-16 BE", EncodingChoice.Utf16Be),
        new("ANSI (Latin-1)", EncodingChoice.Ansi)
    };

    public FormatDialog(FormatSettings format, EncodingChoice left, EncodingChoice right)
    {
        InitializeComponent();
        ThemeService.Track(this);

        LeftEncodingBox.ItemsSource = EncodingItems;
        RightEncodingBox.ItemsSource = EncodingItems;
        LeftEncodingBox.SelectedValue = left;
        RightEncodingBox.SelectedValue = right;

        TabSizeBox.ItemsSource = new[] { 2, 3, 4, 6, 8 };
        TabSizeBox.SelectedItem = TabSizeBox.Items.Contains(format.TabSize) ? format.TabSize : 4;
        WhitespaceBox.IsChecked = format.ShowWhitespace;
        EndOfLineBox.IsChecked = format.ShowEndOfLine;
        SyntaxBox.IsChecked = format.SyntaxHighlighting;
        InlineBox.IsChecked = format.InlineDifferences;
    }

    public FormatSettings Format { get; private set; } = new();
    public EncodingChoice LeftEncoding { get; private set; }
    public EncodingChoice RightEncoding { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Format = new FormatSettings
        {
            TabSize = TabSizeBox.SelectedItem is int size ? size : 4,
            ShowWhitespace = WhitespaceBox.IsChecked == true,
            ShowEndOfLine = EndOfLineBox.IsChecked == true,
            SyntaxHighlighting = SyntaxBox.IsChecked == true,
            InlineDifferences = InlineBox.IsChecked == true
        };
        LeftEncoding = LeftEncodingBox.SelectedValue is EncodingChoice l ? l : EncodingChoice.Detect;
        RightEncoding = RightEncodingBox.SelectedValue is EncodingChoice r ? r : EncodingChoice.Detect;
        DialogResult = true;
    }
}
