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
    public void Handles_lifetimes_raw_strings_and_hash_comments()
    {
        var rows = Equal(
            "fn f<'a>(x: &'a str) -> &'a str {", // 0
            "    x",
            "}",
            "void M()",                           // 3
            "{",
            "    var json = \"\"\"",
            "        { \"a\": \"}\" }",
            "        \"\"\";",
            "    # comment {",
            "}");

        Assert.Equal([new FoldRegion(0, 2, false, true), new FoldRegion(3, 9, false, true)], FoldRegionFinder.Find(rows));
    }

    [Theory]
    [InlineData("    var s = \"a\\\"}\";")]
    [InlineData("    var s = \"\\\\\" + \"}\";")]
    [InlineData("    var s = @\"a\"\"}\"\"\";")]
    [InlineData("    var s = $@\"}\";")]
    [InlineData("    /* } */ /* } */")]
    [InlineData("    /* }*/")]
    [InlineData("    const s = `a\\`}`;")]
    [InlineData("    var s = \"\"\"\"}\"\"\"\";")]
    [InlineData("    var s = \"\" + \"}\";")]
    [InlineData("    var c = '\\'' + '}';")]
    public void Skips_braces_in_tricky_literals(string line)
    {
        var rows = Equal("void M()", "{", line, "    X();", "}");

        Assert.Equal([new FoldRegion(0, 4, false, true)], FoldRegionFinder.Find(rows));
    }

    [Fact]
    public void Multiline_literals_and_comments_hide_braces()
    {
        var rows = Equal(
            "void M()",           // 0
            "{",
            "    var a = @\"",
            "        } \"\" }",
            "        \";",
            "    /*",
            "     } *",
            "     */",
            "    var b = \"\"\"\"",
            "        } \"\"\" }",
            "        \"\"\"\";",
            "    const t = `",
            "        } \\` }",
            "    `;",
            "}");                 // 14

        Assert.Equal([new FoldRegion(0, 14, false, true)], FoldRegionFinder.Find(rows));
    }

    [Fact]
    public void Unterminated_string_ends_at_line_end()
    {
        var rows = Equal("void M()", "{", "    var s = \"{ \\", "    X();", "}");

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

    [Theory]
    [InlineData("    items.Add(new Item")]
    [InlineData("    yield return new Item")]
    [InlineData("    throw new Error")]
    [InlineData("    await Run(new Options")]
    [InlineData("    new Item")]
    [InlineData("    Call(); // note")]
    public void Expression_headers_are_not_declarations(string header)
    {
        var rows = Equal(header, "    {", "        A = 1,", "    });");

        Assert.False(FoldRegionFinder.Find(rows).Single().IsDeclaration);
    }

    [Fact]
    public void Comment_rows_are_not_headers()
    {
        var rows = Equal(
            "void M()",
            "/* note",
            "   more */",
            "// legacy",
            "{",
            "    A();",
            "}");

        Assert.Equal([new FoldRegion(0, 6, false, true)], FoldRegionFinder.Find(rows));
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
