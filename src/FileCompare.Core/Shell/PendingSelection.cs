namespace FileCompare.Core.Shell;

/// <summary>
/// Explorer starts the "Compare selected items" command once per selected item.
/// The running instance collects those paths here and, after a short pause, compares them.
/// </summary>
public sealed class PendingSelection
{
    private readonly List<string> _paths = new();

    public int Count => _paths.Count;

    /// <summary>Adds a path. The same path twice counts once.</summary>
    public void Add(string path)
    {
        if (!_paths.Contains(path, StringComparer.OrdinalIgnoreCase)) _paths.Add(path);
    }

    /// <summary>Returns the collected paths in arrival order and starts over.</summary>
    public IReadOnlyList<string> Take()
    {
        var taken = _paths.ToArray();
        _paths.Clear();
        return taken;
    }
}
