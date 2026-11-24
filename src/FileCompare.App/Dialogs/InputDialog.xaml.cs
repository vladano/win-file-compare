using System.Windows;
using FileCompare.App.Theming;

namespace FileCompare.App.Dialogs;

/// <summary>Asks for one line of text.</summary>
public partial class InputDialog : Window
{
    private InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        ThemeService.Track(this);
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    /// <summary>The text entered, or null when the user cancelled.</summary>
    public static string? Ask(Window? owner, string title, string prompt, string initial = "")
    {
        var dialog = new InputDialog(title, prompt, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.ValueBox.Text : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
