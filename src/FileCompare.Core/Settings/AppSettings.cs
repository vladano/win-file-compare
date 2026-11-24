using FileCompare.Core.Folders;
using FileCompare.Core.Sessions;
using FileCompare.Core.Text;

namespace FileCompare.Core.Settings;

/// <summary>Rules: what counts as an unimportant difference, and how much context to show.</summary>
public sealed class RuleSettings
{
    public bool IgnoreLeadingWhitespace { get; set; }
    public bool IgnoreEmbeddedWhitespace { get; set; }
    public bool IgnoreTrailingWhitespace { get; set; }
    public bool IgnoreCase { get; set; }

    /// <summary>How lines are matched up. Patience often reads better for source code.</summary>
    public DiffAlgorithm Algorithm { get; set; } = DiffAlgorithm.Myers;

    /// <summary>Lines shown around each difference in the Context view.</summary>
    public int ContextLines { get; set; } = 3;

    public DiffOptions ToDiffOptions() => new()
    {
        Algorithm = Algorithm,
        IgnoreLeadingWhitespace = IgnoreLeadingWhitespace,
        IgnoreEmbeddedWhitespace = IgnoreEmbeddedWhitespace,
        IgnoreTrailingWhitespace = IgnoreTrailingWhitespace,
        IgnoreCase = IgnoreCase
    };

    public RuleSettings Clone() => (RuleSettings)MemberwiseClone();
}

/// <summary>Format: how text is displayed in the panes.</summary>
public sealed class FormatSettings
{
    public int TabSize { get; set; } = 4;
    public bool ShowWhitespace { get; set; }
    public bool ShowEndOfLine { get; set; }

    /// <summary>Color source code by the file extension (C#, JSON, XML, Python and more).</summary>
    public bool SyntaxHighlighting { get; set; } = true;

    /// <summary>Mark the characters that changed inside a changed line.</summary>
    public bool InlineDifferences { get; set; } = true;

    public FormatSettings Clone() => (FormatSettings)MemberwiseClone();
}

/// <summary>Folder compare: what to skip and how files are compared.</summary>
public sealed class FolderSettings
{
    /// <summary>Names of files and folders that are left out ("*" and "?" work).</summary>
    public List<string> IgnorePatterns { get; set; } = new() { ".git", "node_modules", "bin", "obj", ".vs" };

    public FolderCompareMode Mode { get; set; } = FolderCompareMode.Content;

    /// <summary>Files read at the same time in a content compare. 0: chosen from the disks (see DiskInfo).</summary>
    public int Parallelism { get; set; }

    public FolderCompareOptions ToOptions() => new()
    {
        IgnorePatterns = IgnorePatterns.ToArray(),
        Parallelism = Parallelism,
        Mode = Mode
    };

    public FolderSettings Clone() => new() { IgnorePatterns = new List<string>(IgnorePatterns), Mode = Mode, Parallelism = Parallelism };
}

/// <summary>User settings that are kept between runs. Add new settings here with a default value.</summary>
public sealed class AppSettings
{
    public const string DefaultTheme = "system";

    /// <summary>Id of the chosen theme. Unknown ids fall back to the system theme in the UI.</summary>
    public string Theme { get; set; } = DefaultTheme;

    public RuleSettings Rules { get; set; } = new();

    public FormatSettings Format { get; set; } = new();

    public FolderSettings Folders { get; set; } = new();

    /// <summary>Comparisons opened recently, newest first.</summary>
    public List<RecentEntry> Recent { get; set; } = new();

    /// <summary>Comparisons saved under a name, sorted by name.</summary>
    public List<SavedSession> Sessions { get; set; } = new();

    /// <summary>A settings file may hold "null" for a list; this puts empty lists back so callers never see null.</summary>
    public AppSettings Normalized()
    {
        Rules ??= new RuleSettings();
        Format ??= new FormatSettings();
        Folders ??= new FolderSettings();
        Folders.IgnorePatterns ??= new List<string>();
        Folders.Parallelism = Math.Clamp(Folders.Parallelism, 0, DiskInfo.MaxParallelism);
        Recent ??= new List<RecentEntry>();
        Sessions ??= new List<SavedSession>();
        return this;
    }
}
