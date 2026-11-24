using System.Windows;

namespace FileCompare.App.Tabs;

/// <summary>Lets a control accept files and folders dragged from File Explorer.</summary>
public static class FileDrop
{
    /// <param name="preview">
    /// Use for controls that handle text drops themselves (text boxes, editors): the files are taken
    /// before the control sees them. Text dragged onto them still works as usual.
    /// </param>
    public static void Attach(UIElement element, Action<string[]> onDrop, bool preview = false)
    {
        element.AllowDrop = true;

        void Over(object sender, DragEventArgs e)
        {
            if (PathsOf(e) is null) return; // not files: leave it to the control
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        void Drop(object sender, DragEventArgs e)
        {
            if (PathsOf(e) is not { } paths) return;
            e.Handled = true;
            // Finish the drop first, so Explorer is not kept waiting while the files are compared.
            element.Dispatcher.BeginInvoke(() => onDrop(paths));
        }

        if (preview)
        {
            element.PreviewDragEnter += Over;
            element.PreviewDragOver += Over;
            element.PreviewDrop += Drop;
        }
        else
        {
            element.DragEnter += Over;
            element.DragOver += Over;
            element.Drop += Drop;
        }
    }

    private static string[]? PathsOf(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths
            ? paths
            : null;
}
