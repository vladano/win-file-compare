namespace FileCompare.App.Editor;

/// <summary>Keeps the scroll position of two panes identical.</summary>
public sealed class ScrollSynchronizer
{
    private bool _syncing;

    public ScrollSynchronizer(DiffPane a, DiffPane b)
    {
        a.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => Sync(a, b);
        b.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => Sync(b, a);
    }

    private void Sync(DiffPane from, DiffPane to)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            to.Editor.ScrollToVerticalOffset(from.Editor.VerticalOffset);
            to.Editor.ScrollToHorizontalOffset(from.Editor.HorizontalOffset);
        }
        finally { _syncing = false; }
    }
}
