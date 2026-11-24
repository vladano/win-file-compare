using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FileCompare.App.Editor;
using FileCompare.Core.Folders;

namespace FileCompare.App.Folders;

/// <summary>One line of the folder grid. Wraps a node and gives the grid ready-to-show text.</summary>
public sealed class FolderRow : INotifyPropertyChanged
{
    private bool _isExpanded;

    public FolderRow(VisibleNode visible)
    {
        Node = visible.Node;
        Depth = visible.Depth;
        HasChildren = visible.HasChildren;
        _isExpanded = visible.IsExpanded;
        _shownStatus = Node.Status;
        _shownMessage = Node.Message;
    }

    public FolderNode Node { get; }
    public int Depth { get; }
    public bool HasChildren { get; }
    public string RelativePath => Node.RelativePath;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            Raise(nameof(IsExpanded));
            Raise(nameof(Expander));
        }
    }

    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    // Segoe MDL2 Assets glyphs: chevron right, chevron down, folder, document.
    public string Expander => !HasChildren ? "" : IsExpanded ? "\uE70D" : "\uE76C";
    public string Glyph => Node.IsFolder ? "\uE8B7" : "\uE8A5";

    private static readonly Brush FolderGlyphBrush = FrozenBrush(Color.FromRgb(240, 165, 30));
    private static readonly Brush FileGlyphBrush = FrozenBrush(Color.FromRgb(140, 150, 165));

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Folders are amber and files grey, which reads on every theme.</summary>
    public Brush GlyphBrush => Node.IsFolder ? FolderGlyphBrush : FileGlyphBrush;

    public string Name => Node.Name;
    public string LeftSize => Node.IsFolder ? "" : SizeText(Node.Left);
    public string RightSize => Node.IsFolder ? "" : SizeText(Node.Right);
    public string LeftDate => DateText(Node.Left);
    public string RightDate => DateText(Node.Right);

    public string StatusText => Node.Status switch
    {
        FolderStatus.Same => "Same",
        FolderStatus.Different => "Different",
        FolderStatus.LeftOnly => "Left only",
        FolderStatus.RightOnly => "Right only",
        FolderStatus.Pending => Node.Message is null ? "Comparing..." : "Not compared",
        _ => "Error"
    };

    /// <summary>Shown when hovering a row; only errors have something to say.</summary>
    public string? ToolTipText => Node.Message;

    public Brush RowBrush => Node.Status switch
    {
        FolderStatus.Different => DiffTheme.ChangedBrush,
        FolderStatus.LeftOnly or FolderStatus.RightOnly => DiffTheme.FolderOnlyBrush,
        FolderStatus.Error => DiffTheme.InlineChangedBrush,
        // Same rows still need a background: the default cell is white, which hides light text on dark themes.
        _ => DiffTheme.EditorBackground
    };

    /// <summary>Call after a theme change so the row picks up the new colors.</summary>
    public void RefreshBrush() => Raise(nameof(RowBrush));

    private FolderStatus _shownStatus;
    private string? _shownMessage;

    /// <summary>
    /// Shows the node's current status if it changed since the row was made (content results arrive while the grid
    /// is shown). Returns true if it changed.
    /// </summary>
    public bool RefreshStatus()
    {
        if (_shownStatus == Node.Status && ReferenceEquals(_shownMessage, Node.Message)) return false;
        _shownStatus = Node.Status;
        _shownMessage = Node.Message;
        Raise(nameof(StatusText));
        Raise(nameof(RowBrush));
        Raise(nameof(ToolTipText));
        return true;
    }

    private static string SizeText(SideInfo? side) => side is null ? "" : FolderFormat.Size(side.Value);

    private static string DateText(SideInfo? side) =>
        side is null ? "" : side.Value.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Text for sizes.</summary>
public static class FolderFormat
{
    /// <summary>Folders have no size to show (their entry has size 0 and no extension), so this is for files.</summary>
    public static string Size(SideInfo side) => Size(side.Size);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes:N0} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):N1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):N2} GB"
    };
}

/// <summary>An observable list that can be replaced in one go (one change notification instead of one per row).</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }
}
