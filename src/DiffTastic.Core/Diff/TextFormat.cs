namespace DiffTastic.Core.Diff;

/// <summary>Encoding and line ending of a file. LineEnding is null when the file has no line breaks.</summary>
public sealed record TextFormat(string Encoding, string? LineEnding)
{
    public static TextFormat Detect(ReadOnlySpan<byte> bytes) => new(DetectEncoding(bytes), DetectLineEnding(bytes));

    private static string DetectEncoding(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => "UTF-8 BOM",
        [0xFF, 0xFE, 0x00, 0x00, ..] => "UTF-32 LE",
        [0xFF, 0xFE, ..] => "UTF-16 LE",
        [0xFE, 0xFF, ..] => "UTF-16 BE",
        _ => "UTF-8",
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
