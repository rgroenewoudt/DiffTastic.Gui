using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Tests;

public class FoldRegionFinderTests
{
    private static List<AlignedRow> Equal(params string[] lines) =>
        lines.Select((t, i) => new AlignedRow(new DiffLine(i, t, []), new DiffLine(i, t, []), RowKind.Equal)).ToList();

    private static void Change(List<AlignedRow> rows, int row) =>
        rows[row] = rows[row] with { Kind = RowKind.Changed };

    [Fact]
    public void Finds_allman_and_knr_blocks()
    {
        var rows = Equal(
            "class A",
            "{",
            "    void M() {",
            "        Call();",
            "    }",
            "}");

        var regions = FoldRegionFinder.Find(rows);

        Assert.Equal([new FoldRegion(0, 5, false, true), new FoldRegion(2, 4, false, true)], regions);
    }

    [Fact]
    public void Ignores_braces_in_strings_and_comments()
    {
        var rows = Equal(
            "void M()",
            "{",
            "    var s = \"{\" + '{' + @\"}\"; // {",
            "    /* } */",
            "}");

        Assert.Equal([new FoldRegion(0, 4, false, true)], FoldRegionFinder.Find(rows));
    }

    [Fact]
    public void Control_flow_and_initializers_are_not_declarations()
    {
        var rows = Equal(
            "void M()",
            "{",
            "    if (x)",
            "    {",
            "        A();",
            "    }",
            "    var list = new List<int>",
            "    {",
            "        1,",
            "    };",
            "    Run(() => {",
            "        B();",
            "    });",
            "}");

        var regions = FoldRegionFinder.Find(rows);

        Assert.True(regions.Single(r => r.StartRow == 0).IsDeclaration);
        Assert.False(regions.Single(r => r.StartRow == 2).IsDeclaration);
        Assert.False(regions.Single(r => r.StartRow == 6).IsDeclaration);
        Assert.False(regions.Single(r => r.StartRow == 10).IsDeclaration);
    }

    [Fact]
    public void Default_folds_unchanged_declarations_outermost_only()
    {
        var rows = Equal(
            "class A",       // 0
            "{",
            "    void M1()", // 2
            "    {",
            "        One();",
            "    }",
            "    void M2()", // 6
            "    {",
            "        Two();",
            "        if (x) {", // 9
            "            Y();",
            "        }",
            "    }",
            "}",             // 13
            "class B",       // 14
            "{",
            "    void M3()",
            "    {",
            "        Three();",
            "    }",
            "}");
        Change(rows, 8);

        var folded = FoldRegionFinder.DefaultFolded(FoldRegionFinder.Find(rows));

        Assert.Equal([(2, 5), (14, 20)], folded.Select(r => (r.StartRow, r.EndRow)));
    }

    [Fact]
    public void Finds_block_present_on_one_side_only()
    {
        var rows = Equal("class A", "{", "}");
        rows.InsertRange(2,
        [
            new AlignedRow(null, new DiffLine(2, "    void Added()", []), RowKind.Added),
            new AlignedRow(null, new DiffLine(3, "    {", []), RowKind.Added),
            new AlignedRow(null, new DiffLine(4, "        X();", []), RowKind.Added),
            new AlignedRow(null, new DiffLine(5, "    }", []), RowKind.Added),
        ]);

        var regions = FoldRegionFinder.Find(rows);

        Assert.Contains(new FoldRegion(2, 5, true, true), regions);
        Assert.Contains(new FoldRegion(0, 6, true, true), regions);
        Assert.Empty(FoldRegionFinder.DefaultFolded(regions));
    }
}
