using DiffTastic.Core.Ai;
using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Tests;

public class DiffAnalysisTests
{
    private static AlignedRow Equal(int i, string text) => new(new DiffLine(i, text, []), new DiffLine(i, text, []), RowKind.Equal);
    private static AlignedRow Changed(int i, string left, string right) => new(new DiffLine(i, left, []), new DiffLine(i, right, []), RowKind.Changed);
    private static AlignedRow Added(int i, string text) => new(null, new DiffLine(i, text, []), RowKind.Added);

    private static DiffDocument Sample() => new(
    [
        Equal(0, "class A"),
        Equal(1, "{"),
        Changed(2, "    int x = 1;", "    int x = 2;"),
        Equal(3, "    void F()"),
        Equal(4, "    {"),
        Equal(5, "    }"),
        Equal(6, "    void G()"),
        Added(7, "    // comment"),
        Equal(8, "}"),
    ], "C#", null);

    [Fact]
    public void Prompt_lists_every_hunk_with_context()
    {
        var prompt = DiffAnalysis.BuildPrompt(Sample(), "A.cs");

        Assert.Contains("File: A.cs (C#), 2 hunks.", prompt);
        Assert.Contains("### Hunk 0\n class A\n {\n-    int x = 1;\n+    int x = 2;\n     void F()\n", prompt);
        Assert.Contains("### Hunk 1\n", prompt);
        Assert.Contains("+    // comment\n }\n", prompt);
        Assert.DoesNotContain("### Hunk 2", prompt);
    }

    [Fact]
    public void Context_stops_at_the_neighbouring_hunk()
    {
        var doc = new DiffDocument(
        [
            Equal(0, "a"),
            Changed(1, "old b", "new b"),
            Equal(2, "c"),
            Changed(3, "old d", "new d"),
            Equal(4, "e"),
        ], "Text", null);

        var prompt = DiffAnalysis.BuildPrompt(doc, "x.txt");

        Assert.Contains("### Hunk 0\n a\n-old b\n+new b\n c\n\n### Hunk 1\n c\n-old d\n+new d\n e\n", prompt);
    }

    [Fact]
    public void Prompt_truncates_long_hunks_but_keeps_all_ids()
    {
        var rows = new List<AlignedRow>();
        for (var i = 0; i < 1000; i++)
            rows.Add(Added(i, $"line {i} " + new string('x', 50)));
        rows.Add(Equal(1000, "end"));
        rows.Add(Added(1001, "last"));
        var doc = new DiffDocument(rows, "Text", null);

        var prompt = DiffAnalysis.BuildPrompt(doc, "big.txt", budget: 10_000);

        Assert.True(prompt.Length <= 10_000, $"length {prompt.Length}");
        Assert.Contains("more lines)", prompt);
        Assert.Contains("### Hunk 1\n", prompt);
        Assert.Contains("+last", prompt);
    }

    [Fact]
    public void Prompt_cuts_very_long_lines()
    {
        var doc = new DiffDocument([Changed(0, new string('a', 100_000), new string('b', 100_000))], "JavaScript", null);

        var prompt = DiffAnalysis.BuildPrompt(doc, "app.min.js", budget: 10_000);

        Assert.True(prompt.Length <= 10_000, $"length {prompt.Length}");
        Assert.Contains("### Hunk 0\n-" + new string('a', DiffAnalysis.MaxLineLength) + "… (99500 more characters)\n", prompt);
        Assert.DoesNotContain("more hunks not shown.", prompt);
    }

    [Fact]
    public void Prompt_leaves_out_hunks_over_budget()
    {
        var rows = new List<AlignedRow>();
        for (var i = 0; i < 3000; i++)
        {
            rows.Add(Equal(2 * i, "same"));
            rows.Add(Changed(2 * i + 1, $"old {i}", $"new {i}"));
        }
        var doc = new DiffDocument(rows, "Text", null);

        var prompt = DiffAnalysis.BuildPrompt(doc, "gen.txt", budget: 10_000);

        Assert.True(prompt.Length <= 10_000, $"length {prompt.Length}");
        Assert.Contains("### Hunk 0\n", prompt);
        Assert.DoesNotContain("### Hunk 2999\n", prompt);
        Assert.Contains("more hunks not shown.", prompt);
    }

    [Fact]
    public void Parse_drops_unknown_and_duplicate_hunks()
    {
        const string json = """
            {
              "summary": " Changes x. ",
              "hunks": [
                { "hunk": 1, "importance": 0, "noise": true, "reason": "Comment only.", "reviewHints": [] },
                { "hunk": 0, "importance": 9, "noise": false, "reason": "Changes a value.", "reviewHints": ["Check callers", " "] },
                { "hunk": 0, "importance": 1, "noise": false, "reason": "dup", "reviewHints": [] },
                { "hunk": 7, "importance": 3, "noise": false, "reason": "unknown", "reviewHints": [] }
              ]
            }
            """;

        var analysis = DiffAnalysis.Parse(json, hunkCount: 2);

        Assert.Equal("Changes x.", analysis.Summary);
        Assert.Equal([0, 1], analysis.Hunks.Select(h => h.Hunk));
        Assert.Equal(3, analysis.NoteFor(0)!.Importance);
        Assert.Equal(["Check callers"], analysis.NoteFor(0)!.ReviewHints);
        Assert.True(analysis.NoteFor(0)!.IsImportant);
        Assert.True(analysis.IsNoise(1));
        Assert.Null(analysis.NoteFor(7));
    }

    [Fact]
    public void Parse_rejects_invalid_json()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => DiffAnalysis.Parse("not json", 1));
    }

    [Fact]
    public async Task Analyzer_caches_answers()
    {
        var provider = new FakeProvider("""{"summary":"s","hunks":[{"hunk":0,"importance":2,"noise":false,"reason":"r","reviewHints":[]}]}""");
        var analyzer = new DiffAnalyzer(provider);

        var first = await analyzer.AnalyzeAsync(Sample(), "A.cs");
        var second = await analyzer.AnalyzeAsync(Sample(), "A.cs");

        Assert.Equal(1, provider.Calls);
        Assert.Same(first, second);
        Assert.Equal(DiffAnalysis.Schema, provider.LastSchema);
    }

    [Fact]
    public async Task Analyzer_skips_identical_files()
    {
        var provider = new FakeProvider("{}");
        var analysis = await new DiffAnalyzer(provider).AnalyzeAsync(new DiffDocument([Equal(0, "a")], "Text", null), "a.txt");

        Assert.Equal(0, provider.Calls);
        Assert.Empty(analysis.Hunks);
    }

    private sealed class FakeProvider(string answer) : AiProvider
    {
        public int Calls { get; private set; }
        public string? LastSchema { get; private set; }
        public override string Name => "Fake";
        public override string Model => "fake";
        public override string Vendor => "nobody";

        public override Task<string> CompleteJsonAsync(string prompt, string schema, CancellationToken ct = default)
        {
            Calls++;
            LastSchema = schema;
            return Task.FromResult(answer);
        }
    }
}
