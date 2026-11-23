using DiffTastic.Core.Ai;
using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Tests;

public class DiffQuestionerTests
{
    private static AlignedRow Equal(int i, string text) => new(new DiffLine(i, text, []), new DiffLine(i, text, []), RowKind.Equal);
    private static AlignedRow Changed(int i, string left, string right) => new(new DiffLine(i, left, []), new DiffLine(i, right, []), RowKind.Changed);

    private static DiffDocument Sample() => new(
    [
        Equal(0, "class A"),
        Equal(1, "{"),
        Changed(2, "    int x = 1;", "    int x = 2;"),
        Equal(3, "}"),
    ], "C#", null);

    [Fact]
    public void Block_context_shows_old_and_new_lines_with_context()
    {
        var context = DiffQuestioner.BlockContext(Sample(), 0);

        Assert.Equal(" class A\n {\n-    int x = 1;\n+    int x = 2;\n }\n", context);
    }

    [Fact]
    public void Selection_context_adds_partly_selected_text()
    {
        var context = DiffQuestioner.SelectionContext([('+', "    int x = 2;")], "x = 2");

        Assert.Equal("+    int x = 2;\n\nSelected text: x = 2\n", context);
    }

    [Fact]
    public void Selection_context_is_limited()
    {
        var lines = Enumerable.Range(0, DiffQuestioner.MaxContextLines + 5).Select(i => (' ', $"line {i}"));

        var context = DiffQuestioner.SelectionContext(lines);

        Assert.Contains($"line {DiffQuestioner.MaxContextLines - 1}\n", context);
        Assert.DoesNotContain($"line {DiffQuestioner.MaxContextLines}\n", context);
        Assert.EndsWith("… (more lines not shown)\n", context);
    }

    [Fact]
    public void Prompt_holds_file_context_question_and_language()
    {
        var prompt = DiffQuestioner.BuildPrompt("A.cs", "+    int x = 2;\n", "  Why 2?  ", "Dutch");

        Assert.Contains("diff of the file A.cs.", prompt);
        Assert.Contains("+    int x = 2;\n\nQuestion: Why 2?", prompt);
        Assert.Contains("Write the answer in Dutch.", prompt);
    }

    [Fact]
    public async Task Answers_are_cached_per_question()
    {
        var ai = new CountingAi("""{ "answer": " Because. " }""");
        var questioner = new DiffQuestioner(ai);

        Assert.Equal("Because.", await questioner.AskAsync("A.cs", "+x\n", "Why?"));
        Assert.Equal("Because.", await questioner.AskAsync("A.cs", "+x\n", "Why?"));
        await questioner.AskAsync("A.cs", "+x\n", "What?");

        Assert.Equal(2, ai.Calls);
        Assert.Equal(DiffQuestioner.Schema, ai.LastSchema);
    }

    [Fact]
    public async Task Empty_answer_fails()
    {
        var questioner = new DiffQuestioner(new CountingAi("""{ "answer": "" }"""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => questioner.AskAsync("A.cs", "+x\n", "Why?"));
    }

    private sealed class CountingAi(string answer) : AiProvider
    {
        public int Calls { get; private set; }
        public string? LastSchema { get; private set; }
        public override string Name => "Counting";
        public override string Model => "c-1";
        public override string Vendor => "nobody";

        public override Task<string> CompleteJsonAsync(string prompt, string schema, CancellationToken ct = default)
        {
            Calls++;
            LastSchema = schema;
            return Task.FromResult(answer);
        }
    }
}
