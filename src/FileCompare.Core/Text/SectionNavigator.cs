namespace FileCompare.Core.Text;

/// <summary>Walks the difference sections of a <see cref="DiffResult"/>, wrapping at both ends.</summary>
public sealed class SectionNavigator
{
    private readonly DiffResult? _result;
    private int _current = -1;

    public SectionNavigator(DiffResult? result) => _result = result;

    public int Count => _result?.DifferenceCount ?? 0;

    /// <summary>Zero-based index of the current section, or -1 before the first move.</summary>
    public int Current => _current;

    /// <summary>Moves by <paramref name="direction"/> sections. Returns the row index to show, or null if there are no differences.</summary>
    public int? Move(int direction)
    {
        if (_result is null || Count == 0) return null;
        _current = ((_current + direction) % Count + Count) % Count;
        return _result.SectionStarts[_current];
    }
}
