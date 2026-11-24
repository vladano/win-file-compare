using System.Text;

namespace FileCompare.Core.Text;

public enum LineEnding { Unix, Windows, Mac, Mixed, None }

public sealed record LoadedText(
    string Path,
    string Text,
    string EncodingName,
    LineEnding LineEnding,
    long SizeBytes,
    DateTime LastWriteTime,
    FileEncoding FileEncoding);

public static class TextFileLoader
{
    /// <param name="choice">Force an encoding, or <see cref="EncodingChoice.Detect"/> to detect it.</param>
    public static LoadedText Load(string path, EncodingChoice choice = EncodingChoice.Detect)
    {
        var info = new FileInfo(path);
        byte[] bytes;
        // Allow reading files that other programs have open.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var ms = new MemoryStream())
        {
            fs.CopyTo(ms);
            bytes = ms.ToArray();
        }

        var encoding = choice == EncodingChoice.Detect ? FileEncoding.Detect(bytes) : FileEncoding.For(choice, bytes);
        int bomLength = choice == EncodingChoice.Detect ? encoding.Preamble.Length : encoding.BomLengthIn(bytes);
        string text = encoding.Encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        return new LoadedText(path, text, encoding.Name, DetectLineEnding(text), bytes.LongLength, info.LastWriteTime, encoding);
    }

    /// <summary>
    /// True when the start of the file looks like binary data (it has zero bytes and is not UTF-16 text).
    /// Files that cannot be read count as text, so the normal error message appears later.
    /// </summary>
    public static bool LooksBinary(string path)
    {
        try
        {
            var buffer = new byte[8000];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            var bytes = buffer[..read];
            if (FileEncoding.Detect(bytes).Name.StartsWith("UTF-16", StringComparison.Ordinal)) return false;
            return Array.IndexOf(bytes, (byte)0) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static LineEnding DetectLineEnding(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }

        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        if (kinds == 0) return LineEnding.None;
        if (kinds > 1) return LineEnding.Mixed;
        return crlf > 0 ? LineEnding.Windows : lf > 0 ? LineEnding.Unix : LineEnding.Mac;
    }
}
