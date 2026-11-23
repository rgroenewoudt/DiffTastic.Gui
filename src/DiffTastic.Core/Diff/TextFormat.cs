using System.Text;
using System.Text.Unicode;

namespace DiffTastic.Core.Diff;

/// <summary>Encoding and line ending of a file. LineEnding is null when the file has no line breaks.</summary>
public sealed record TextFormat(string Encoding, string? LineEnding)
{
    public const string Ansi = "ANSI";

    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static TextFormat Detect(ReadOnlySpan<byte> bytes) => new(DetectEncoding(bytes), DetectLineEnding(bytes));

    /// <summary>Files without BOM that are not valid UTF-8 are read as Windows-1252.</summary>
    public string Decode(byte[] bytes)
    {
        if (Encoding == Ansi)
            return Windows1252.GetString(bytes);

        using var reader = new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string DetectEncoding(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => "UTF-8 BOM",
        [0xFF, 0xFE, 0x00, 0x00, ..] => "UTF-32 LE",
        [0xFF, 0xFE, ..] => "UTF-16 LE",
        [0xFE, 0xFF, ..] => "UTF-16 BE",
        _ when Utf8.IsValid(bytes) => "UTF-8",
        _ => Ansi,
    };

    private static string? DetectLineEnding(ReadOnlySpan<byte> bytes)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == '\r')
            {
                if (i + 1 < bytes.Length && bytes[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                    cr++;
            }
            else if (bytes[i] == '\n')
                lf++;
        }

        return (crlf, lf, cr) switch
        {
            (0, 0, 0) => null,
            (> 0, 0, 0) => "CRLF",
            (0, > 0, 0) => "LF",
            (0, 0, > 0) => "CR",
            _ => "Mixed",
        };
    }
}
