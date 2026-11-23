using System.Text;
using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Tests;

public class TextFormatTests
{
    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' }, "UTF-8 BOM")]
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'a', 0 }, "UTF-16 LE")]
    [InlineData(new byte[] { 0xFE, 0xFF, 0, (byte)'a' }, "UTF-16 BE")]
    [InlineData(new byte[] { (byte)'a' }, "UTF-8")]
    public void Detect_encoding_from_bom(byte[] bytes, string expected) =>
        Assert.Equal(expected, TextFormat.Detect(bytes).Encoding);

    [Theory]
    [InlineData("a\r\nb\r\n", "CRLF")]
    [InlineData("a\nb\n", "LF")]
    [InlineData("a\rb\r", "CR")]
    [InlineData("a\r\nb\n", "Mixed")]
    [InlineData("a", null)]
    public void Detect_line_ending(string text, string? expected) =>
        Assert.Equal(expected, TextFormat.Detect(Encoding.UTF8.GetBytes(text)).LineEnding);

    [Fact]
    public async Task CompareAsync_reports_format_of_both_files()
    {
        var dir = Directory.CreateTempSubdirectory("difftastic-format");
        try
        {
            var left = Path.Combine(dir.FullName, "left.txt");
            var right = Path.Combine(dir.FullName, "right.txt");
            await File.WriteAllTextAsync(left, "a\r\nb\r\n", new UTF8Encoding(true));
            await File.WriteAllTextAsync(right, "a\nb\n", new UTF8Encoding(false));

            var doc = await new DiffService(null).CompareAsync(left, right);

            Assert.Equal(new TextFormat("UTF-8 BOM", "CRLF"), doc.LeftFormat);
            Assert.Equal(new TextFormat("UTF-8", "LF"), doc.RightFormat);
            Assert.Equal("a", doc.Rows[0].Left!.Text);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
