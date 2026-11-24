using System.Text;

namespace FileCompare.Core.Text;

/// <summary>Encoding the user can force for a file instead of auto detection.</summary>
public enum EncodingChoice
{
    Detect,
    Utf8,
    Utf8Bom,
    Utf16Le,
    Utf16Be,
    Ansi
}

/// <summary>An encoding together with the byte order mark (if any) so a file can be saved the way it was loaded.</summary>
public sealed record FileEncoding(Encoding Encoding, string Name, byte[] Preamble)
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };
    private static readonly byte[] Utf16LeBom = { 0xFF, 0xFE };
    private static readonly byte[] Utf16BeBom = { 0xFE, 0xFF };

    public bool HasBom => Preamble.Length > 0;

    public static FileEncoding Utf8NoBom() => new(new UTF8Encoding(false), "UTF-8", Array.Empty<byte>());

    public static FileEncoding Ansi() => new(Encoding.Latin1, "ANSI", Array.Empty<byte>());

    /// <summary>
    /// The encoding for a forced <paramref name="choice"/>. UTF-16 keeps a BOM only when the file already
    /// starts with one, so forcing an encoding never adds bytes the file did not have.
    /// </summary>
    public static FileEncoding For(EncodingChoice choice, byte[] fileBytes) => choice switch
    {
        EncodingChoice.Utf8 => Utf8NoBom(),
        EncodingChoice.Utf8Bom => new FileEncoding(new UTF8Encoding(false), "UTF-8 BOM", Utf8Bom),
        EncodingChoice.Utf16Le => new FileEncoding(Encoding.Unicode, "UTF-16 LE", StartsWith(fileBytes, Utf16LeBom) ? Utf16LeBom : Array.Empty<byte>()),
        EncodingChoice.Utf16Be => new FileEncoding(Encoding.BigEndianUnicode, "UTF-16 BE", StartsWith(fileBytes, Utf16BeBom) ? Utf16BeBom : Array.Empty<byte>()),
        EncodingChoice.Ansi => Ansi(),
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "Use detection for EncodingChoice.Detect.")
    };

    /// <summary>Auto detection: BOM first, then strict UTF-8, otherwise ANSI.</summary>
    public static FileEncoding Detect(byte[] bytes)
    {
        if (StartsWith(bytes, Utf8Bom)) return new FileEncoding(new UTF8Encoding(false), "UTF-8 BOM", Utf8Bom);
        if (StartsWith(bytes, Utf16LeBom)) return new FileEncoding(Encoding.Unicode, "UTF-16 LE", Utf16LeBom);
        if (StartsWith(bytes, Utf16BeBom)) return new FileEncoding(Encoding.BigEndianUnicode, "UTF-16 BE", Utf16BeBom);

        try
        {
            // Strict validation: throws if the bytes are not valid UTF-8.
            new UTF8Encoding(false, true).GetCharCount(bytes);
            return Utf8NoBom();
        }
        catch (DecoderFallbackException)
        {
            return Ansi();
        }
    }

    /// <summary>Number of leading bytes to skip when decoding: any BOM that belongs to this encoding family.</summary>
    public int BomLengthIn(byte[] bytes) => Encoding.CodePage switch
    {
        65001 when StartsWith(bytes, Utf8Bom) => Utf8Bom.Length,
        1200 when StartsWith(bytes, Utf16LeBom) => Utf16LeBom.Length,
        1201 when StartsWith(bytes, Utf16BeBom) => Utf16BeBom.Length,
        _ => 0
    };

    private static bool StartsWith(byte[] bytes, byte[] prefix) =>
        bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}
