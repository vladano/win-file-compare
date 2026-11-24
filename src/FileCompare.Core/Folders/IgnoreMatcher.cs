namespace FileCompare.Core.Folders;

/// <summary>Matches file and folder names against simple patterns: "*" is any text, "?" is one character, case is ignored.</summary>
public sealed class IgnoreMatcher
{
    private readonly string[] _patterns;

    /// <param name="patterns">Blank lines and lines starting with "#" are skipped.</param>
    public IgnoreMatcher(IEnumerable<string> patterns) =>
        _patterns = patterns.Select(p => p.Trim()).Where(p => p.Length > 0 && !p.StartsWith('#')).ToArray();

    public bool IsIgnored(string name)
    {
        foreach (var pattern in _patterns)
            if (Matches(pattern, name)) return true;
        return false;
    }

    /// <summary>Wildcard match without regular expressions (so a pattern can never throw or run away).</summary>
    internal static bool Matches(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, restart = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*') { star = p++; restart = t; }
            else if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t]))) { p++; t++; }
            else if (star >= 0) { p = star + 1; t = ++restart; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
