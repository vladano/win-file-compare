using System.Text;

namespace FileCompare.Core.Text;

/// <summary>Writes text back to disk with the encoding, BOM and line ending the file was loaded with.</summary>
public static class TextFileSaver
{
    /// <param name="text">Text with "\n" line breaks.</param>
    /// <exception cref="EncoderFallbackException">The text has characters the encoding cannot store (for example in ANSI).</exception>
    public static void Save(string path, string text, FileEncoding encoding, LineEnding lineEnding)
    {
        string eol = lineEnding switch
        {
            LineEnding.Unix => "\n",
            LineEnding.Mac => "\r",
            _ => "\r\n" // Windows, Mixed and None (files that had no line break yet)
        };

        string output = eol == "\n" ? text : text.Replace("\n", eol);

        // A copy with an exception fallback, so unsupported characters fail loudly instead of becoming '?'.
        var strict = Encoding.GetEncoding(encoding.Encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        byte[] body = strict.GetBytes(output);

        var bytes = new byte[encoding.Preamble.Length + body.Length];
        encoding.Preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, encoding.Preamble.Length);
        File.WriteAllBytes(path, bytes);
    }
}
