using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Tests;

public class MoveDetectorTests
{
    private static readonly string[] Method =
    [
        "void Helper()",
        "{",
        "    Console.WriteLine(1);",
        "}",
    ];

    private static IEnumerable<int> MovedLeft(DiffDocument doc) =>
        doc.Rows.Where(r => r.Left?.Moved == true).Select(r => r.Left!.LineIndex);

    private static IEnumerable<int> MovedRight(DiffDocument doc) =>
        doc.Rows.Where(r => r.Right?.Moved == true).Select(r => r.Right!.LineIndex);

    [Fact]
    public void Marks_code_moved_to_another_place()
    {
        string[] left = [.. Method, "", "int a;", "int b;", "int c;"];
        string[] right = ["int a;", "int b;", "int c;", "", .. Method.Select(l => "    " + l)];

        var doc = DiffService.Fallback(left, right, "test");

        Assert.Equal([0, 1, 2, 3], MovedLeft(doc));
        Assert.Equal([4, 5, 6, 7], MovedRight(doc));
        Assert.Equal(4, doc.MovedLines);
    }

    [Fact]
    public void Ignores_short_runs()
    {
        var doc = DiffService.Fallback(
            ["x = 1;", "}", "a", "b", "c", "d"],
            ["a", "b", "c", "d", "x = 1;", "}"],
            "test");

        Assert.Empty(MovedLeft(doc));
        Assert.Equal(0, doc.MovedLines);
    }

    [Fact]
    public void Ignores_runs_without_code()
    {
        var doc = DiffService.Fallback(
            ["}", "}", "}", "a", "b", "c", "d"],
            ["a", "b", "c", "d", "}", "}", "}"],
            "test");

        Assert.Empty(MovedLeft(doc));
    }

    [Fact]
    public void Reindent_in_the_same_block_is_not_a_move()
    {
        var doc = DiffService.Fallback(
            ["start", .. Method, "end"],
            ["start", "if (x)", "{", .. Method.Select(l => "    " + l), "}", "end"],
            "test");

        Assert.Empty(MovedLeft(doc));
    }

    [Fact]
    public void Unchanged_rows_are_not_marked()
    {
        var doc = DiffService.Fallback([.. Method, "a"], [.. Method, "b"], "test");

        Assert.Empty(MovedLeft(doc));
        Assert.Empty(MovedRight(doc));
    }
}
