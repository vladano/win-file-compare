namespace FileCompare.Core.Sessions;

/// <summary>A comparison that was opened recently.</summary>
public sealed class RecentEntry
{
    public CompareKind Kind { get; set; }
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
    public DateTime LastUsed { get; set; }
}

/// <summary>A comparison the user saved under a name, to open again later.</summary>
public sealed class SavedSession
{
    public string Name { get; set; } = "";
    public CompareKind Kind { get; set; }
    public string Left { get; set; } = "";
    public string Right { get; set; } = "";
    public DateTime Saved { get; set; }
}

/// <summary>Rules for the recent list and the saved sessions. The lists themselves live in the settings.</summary>
public static class History
{
    public const int MaxRecent = 20;

    /// <summary>Puts a comparison at the top of the list (moving it if it is there already) and keeps the list short.</summary>
    public static void AddRecent(List<RecentEntry> recent, CompareKind kind, string left, string right, DateTime now)
    {
        if (left.Length == 0 || right.Length == 0) return;

        recent.RemoveAll(e => SamePath(e.Left, left) && SamePath(e.Right, right));
        recent.Insert(0, new RecentEntry { Kind = kind, Left = left, Right = right, LastUsed = now });
        if (recent.Count > MaxRecent) recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
    }

    /// <summary>Saves a session, replacing one with the same name (case does not matter). The list stays sorted by name.</summary>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    public static void SaveSession(List<SavedSession> sessions, string name, CompareKind kind, string left, string right, DateTime now)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("A session needs a name.", nameof(name));

        sessions.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        sessions.Add(new SavedSession { Name = name, Kind = kind, Left = left, Right = right, Saved = now });
        sessions.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
    }

    public static bool HasSession(IEnumerable<SavedSession> sessions, string name) =>
        sessions.Any(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool RemoveSession(List<SavedSession> sessions, string name) =>
        sessions.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>"a.txt ↔ b.txt", used as the default session name and in menus.</summary>
    public static string SuggestName(string left, string right) => $"{ShortName(left)} ↔ {ShortName(right)}";

    /// <summary>The file or folder name, or the whole path for a drive root such as C:\.</summary>
    public static string ShortName(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.Length > 0 ? name : path;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);
}
