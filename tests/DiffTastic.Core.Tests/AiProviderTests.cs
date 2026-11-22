using DiffTastic.Core.Ai;

namespace DiffTastic.Core.Tests;

public class AiProviderTests
{
    [Fact]
    public void Claude_envelope_prefers_structured_output()
    {
        const string stdout = """
            {"type":"result","is_error":false,"result":"{\"summary\":\"text\"}","structured_output":{"summary":"hi","hunks":[]}}
            """;
        Assert.Equal("""{"summary":"hi","hunks":[]}""", ClaudeProvider.ParseEnvelope(stdout));
    }

    [Fact]
    public void Claude_envelope_falls_back_to_result()
    {
        const string stdout = """{"is_error":false,"result":"{\"summary\":\"hi\"}"}""";
        Assert.Equal("""{"summary":"hi"}""", ClaudeProvider.ParseEnvelope(stdout));
    }

    [Fact]
    public void Claude_envelope_error_throws_with_message()
    {
        const string stdout = """{"is_error":true,"result":"Not logged in"}""";
        var ex = Assert.Throws<InvalidOperationException>(() => ClaudeProvider.ParseEnvelope(stdout));
        Assert.Contains("Not logged in", ex.Message);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("""{"a":"b"}""", """
        "{\"a\":\"b\"}"
        """)]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    public void Quote_follows_msvc_rules(string arg, string expected) =>
        Assert.Equal(expected, ProcessRunner.Quote(arg));

    [Fact]
    public void Batch_files_run_via_cmd()
    {
        var psi = ProcessRunner.CreateStartInfo(@"C:\npm\codex.cmd", ["exec", "-", "--model", "a b"]);
        Assert.Equal("cmd.exe", psi.FileName);
        Assert.Equal("/d /s /c \"C:\\npm\\codex.cmd exec - --model \"a b\"\"", psi.Arguments);
    }

    [Fact]
    public void Batch_files_reject_cmd_special_characters()
    {
        Assert.Throws<InvalidOperationException>(() => ProcessRunner.CreateStartInfo("claude.cmd", ["a & b"]));
    }

    [Fact]
    public void Finds_command_on_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "difftastic-path-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var name = OperatingSystem.IsWindows() ? "fakeai.cmd" : "fakeai";
            File.WriteAllText(Path.Combine(dir, name), "");
            Assert.Equal(Path.Combine(dir, name), AiLocator.FindOnPath("fakeai", "/does/not/exist" + Path.PathSeparator + dir));
            Assert.Null(AiLocator.FindOnPath("missing", dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Exe_later_on_path_wins_over_cmd_shim()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var shims = Path.Combine(Path.GetTempPath(), "difftastic-shims-" + Guid.NewGuid().ToString("N")[..8]);
        var native = Path.Combine(Path.GetTempPath(), "difftastic-native-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(shims);
        Directory.CreateDirectory(native);
        try
        {
            File.WriteAllText(Path.Combine(shims, "fakeai.cmd"), "");
            File.WriteAllText(Path.Combine(native, "fakeai.exe"), "");
            Assert.Equal(Path.Combine(native, "fakeai.exe"), AiLocator.FindOnPath("fakeai", shims + Path.PathSeparator + native));
        }
        finally
        {
            Directory.Delete(shims, true);
            Directory.Delete(native, true);
        }
    }

    [Fact]
    public void Claude_schema_is_minified()
    {
        Assert.Equal("""{"a":[1,2]}""", ClaudeProvider.MinifyJson("{ \"a\" : [ 1, 2 ] }"));
    }

    [Fact]
    public async Task Early_exit_returns_the_process_output_instead_of_a_pipe_error()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var result = await ProcessRunner.RunAsync(
            "cmd.exe", ["/d", "/c", "echo not logged in 1>&2 & exit 3"], new string('x', 4_000_000),
            Path.GetTempPath(), TimeSpan.FromSeconds(30));

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("not logged in", result.Stderr);
    }
}
