using DiffTastic.Core.Diff;
using DiffTastic.Core.Difft;

namespace DiffTastic.Core.Tests;

public class LineAlignerTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static IReadOnlyList<AlignedRow> AlignFixture()
    {
        var left = LineAligner.SplitLines(File.ReadAllText(Fixture("old.cs")));
        var right = LineAligner.SplitLines(File.ReadAllText(Fixture("new.cs")));
        var result = DifftResult.Parse(File.ReadAllText(Fixture("old-new.json")));
        return LineAligner.Align(left, right, result);
    }

    [Fact]
    public void SplitLines_drops_trailing_newline()
    {
        Assert.Equal(["a", "b"], LineAligner.SplitLines("a\r\nb\r\n"));
        Assert.Equal(["a", "b"], LineAligner.SplitLines("a\nb"));
        Assert.Equal([""], LineAligner.SplitLines(""));
    }

    [Fact]
    public void Align_covers_every_line_once()
    {
        var rows = AlignFixture();

        Assert.Equal(Enumerable.Range(0, 18), rows.Where(r => r.Left != null).Select(r => r.Left!.LineIndex));
        Assert.Equal(Enumerable.Range(0, 19), rows.Where(r => r.Right != null).Select(r => r.Right!.LineIndex));
    }

    [Fact]
    public void Align_sets_row_kinds()
    {
        var rows = AlignFixture();

        Assert.Equal(RowKind.Equal, rows[0].Kind);
        Assert.Equal(RowKind.Added, rows[1].Kind);
        Assert.Equal("using System.Linq;", rows[1].Right!.Text);

        var addRow = rows.Single(r => r.Right?.LineIndex == 7);
        Assert.Equal(RowKind.Changed, addRow.Kind);
        Assert.Empty(addRow.Left!.Changes);
        Assert.Contains(new TextRange(31, 1), addRow.Right!.Changes);

        var subtract = rows.Single(r => r.Left?.LineIndex == 11);
        Assert.Equal(RowKind.Removed, subtract.Kind);
        Assert.Null(subtract.Right);
    }

    [Fact]
    public void Blocks_group_consecutive_changes()
    {
        var doc = new DiffDocument(AlignFixture(), "C#", null);

        Assert.Equal(new DiffBlock(1, 1), doc.Blocks[0]);
        Assert.True(doc.Blocks.Count >= 4);
    }

    [Fact]
    public void Missing_aligned_lines_means_one_to_one()
    {
        var rows = LineAligner.Align(["a", "b"], ["a", "b"], new DifftResult(null, null, "Text", "unchanged"));

        Assert.All(rows, r => Assert.Equal(RowKind.Equal, r.Kind));
        Assert.Equal(2, rows.Count);
    }

    [Theory]
    [InlineData("x = \"héllo\" + a", 15, 14)]
    [InlineData("x = \"😀\" + a", 13, 11)]
    [InlineData("abc", 99, 3)]
    public void Utf8_offsets_map_to_utf16(string text, int bytes, int expected)
    {
        Assert.Equal(expected, LineAligner.Utf8ToUtf16Index(text, bytes));
    }

    [Fact]
    public void Fallback_marks_changes()
    {
        var doc = DiffService.Fallback(["a", "b", "c"], ["a", "B", "c", "d"], "note");

        Assert.Equal(RowKind.Equal, doc.Rows[0].Kind);
        Assert.Equal(RowKind.Changed, doc.Rows[1].Kind);
        Assert.Equal(RowKind.Added, doc.Rows[^1].Kind);
        Assert.Equal("note", doc.EngineNote);
    }
}

public class DiffServiceTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public async Task Without_difft_uses_line_diff()
    {
        var doc = await new DiffService(null).CompareAsync(Fixture("old.cs"), Fixture("new.cs"));

        Assert.NotNull(doc.EngineNote);
        Assert.Contains(doc.Rows, r => r.Kind == RowKind.Added && r.Right!.Text == "using System.Linq;");
    }

    [Fact]
    public async Task Broken_difft_falls_back()
    {
        var doc = await new DiffService(new DifftRunner("C:/does/not/exist/difft.exe")).CompareAsync(Fixture("old.cs"), Fixture("new.cs"));

        Assert.NotNull(doc.EngineNote);
        Assert.NotEmpty(doc.Blocks);
    }
}
