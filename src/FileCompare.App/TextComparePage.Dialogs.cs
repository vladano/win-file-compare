using System.Windows;
using FileCompare.App.Dialogs;

namespace FileCompare.App;

// The Rules and Format dialogs.
public partial class TextComparePage
{
    private void OnRules(object sender, RoutedEventArgs e)
    {
        var dialog = new RulesDialog(Rules) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return;

        UserSettings.Current.Rules = dialog.Result;
        UserSettings.Save();

        if (_session is null) return;
        SyncFromPanes();
        _session.Recompare(Rules.ToDiffOptions());
        ShowSession(keepScroll: true);
    }

    private void OnFormat(object sender, RoutedEventArgs e)
    {
        var dialog = new FormatDialog(UserSettings.Current.Format, _leftEncoding, _rightEncoding) { Owner = OwnerWindow };
        if (dialog.ShowDialog() != true) return;

        UserSettings.Current.Format = dialog.Format;
        UserSettings.Save();
        ApplyFormat();

        bool encodingChanged = dialog.LeftEncoding != _leftEncoding || dialog.RightEncoding != _rightEncoding;
        if (!encodingChanged || !ConfirmDiscardChanges()) return;

        // A different encoding means decoding the bytes again, so the files are reloaded from disk.
        _leftEncoding = dialog.LeftEncoding;
        _rightEncoding = dialog.RightEncoding;
        Compare(confirmDiscard: false);
    }
}
