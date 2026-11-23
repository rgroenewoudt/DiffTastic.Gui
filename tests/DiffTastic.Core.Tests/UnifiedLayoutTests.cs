using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Tests;

public class UnifiedLayoutTests
{
    [Fact]
    public void Lists_old_lines_before_new_lines_per_block()
    {
        var doc = DiffService.Fallback(["a", "b", "c", "d", "e"], ["a", "B", "C", "x", "d", "f"], "test");
        var layout = new UnifiedLayout(doc);

        var texts = layout.Rows.Select(r => (r.Left is null ? "+" : r.Right is null ? "-" : " ") + (r.Right ?? r.Left)!.Text);
        Assert.Equal([" a", "-b", "-c", "+B", "+C", "+x", " d", "-e", "+f"], texts);
        Assert.Equal(doc.Blocks.Count, layout.Blocks.Count);
        Assert.Equal(new DiffBlock(1, 5), layout.Blocks[0]);
        Assert.Equal(new DiffBlock(7, 8), layout.Blocks[1]);
    }

    [Fact]
    public void Maps_rows_both_ways()
    {
        var doc = DiffService.Fallback(["a", "b", "c"], ["a", "B", "c"], "test");
        var layout = new UnifiedLayout(doc);

        Assert.Equal(4, layout.Rows.Count);
        Assert.Equal([0, 1, 1, 2], Enumerable.Range(0, 4).Select(layout.SourceRow));
        Assert.Equal(0, layout.RowOf(0));
        Assert.Equal(1, layout.RowOf(1));
        Assert.Equal(3, layout.RowOf(2));
    }

    [Fact]
    public void Handles_empty_document()
    {
        var layout = new UnifiedLayout(DiffService.Fallback([], [], "test"));

        Assert.Empty(layout.Rows);
        Assert.Equal(0, layout.SourceRow(0));
        Assert.Equal(0, layout.RowOf(0));
    }
}
