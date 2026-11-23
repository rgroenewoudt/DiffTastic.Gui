using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Tests;

public class DiffDocumentTests
{
    [Fact]
    public void IsWhitespaceOnly_detects_indent_and_blank_line_changes()
    {
        var doc = DiffService.Fallback(
            ["a", "    b", "c", "d", "e"],
            ["a", "\tb", "c", "", "d", "x"],
            "test");

        Assert.Equal(3, doc.Blocks.Count);
        Assert.True(doc.IsWhitespaceOnly(0));
        Assert.True(doc.IsWhitespaceOnly(1));
        Assert.False(doc.IsWhitespaceOnly(2));
        Assert.False(doc.IsWhitespaceOnly(3));
    }

    [Fact]
    public void Counts_added_and_removed_lines()
    {
        var doc = DiffService.Fallback(["a", "b", "c", "d"], ["a", "B", "c", "x", "y"], "test");

        Assert.Equal(3, doc.AddedLines);
        Assert.Equal(2, doc.RemovedLines);
    }
}
