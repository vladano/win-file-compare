using System.Text;
using System.Text.RegularExpressions;

namespace FileCompare.Core.Text;

/// <summary>Splitting text into lines, and normalizing lines according to the Rules.</summary>
internal static class LineTools
{
    private static readonly Regex LineBreak = new("\r\n|\r|\n", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Splits on any line ending. Empty text has no lines; a trailing line break leaves a final empty line.</summary>
    public static string[] Split(string text) => text.Length == 0 ? Array.Empty<string>() : LineBreak.Split(text);

    /// <summary>Converts every line ending to "\n".</summary>
    public static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>Returns the text two lines are compared by. Equal results mean "same as far as the Rules care".</summary>
    public static string Normalize(string line, DiffOptions options)
    {
        if (options.IgnoreWhitespace)
        {
            var sb = new StringBuilder(line.Length);
            foreach (char c in line)
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            line = sb.ToString();
        }
        else if (options.IgnoreLeadingWhitespace || options.IgnoreTrailingWhitespace || options.IgnoreEmbeddedWhitespace)
        {
            int start = 0;
            while (start < line.Length && char.IsWhiteSpace(line[start])) start++;
            int end = line.Length;
            while (end > start && char.IsWhiteSpace(line[end - 1])) end--;

            string lead = options.IgnoreLeadingWhitespace ? "" : line[..start];
            string core = line[start..end];
            string trail = options.IgnoreTrailingWhitespace ? "" : line[end..];
            if (options.IgnoreEmbeddedWhitespace) core = WhitespaceRun.Replace(core, " ");
            line = lead + core + trail;
        }

        return options.IgnoreCase ? line.ToUpperInvariant() : line;
    }
}
