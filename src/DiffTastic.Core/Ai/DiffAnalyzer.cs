using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using DiffTastic.Core.Diff;

namespace DiffTastic.Core.Ai;

/// <summary>Asks the AI about a diff; answers are cached per prompt, so reloading costs nothing.</summary>
public sealed class DiffAnalyzer(AiProvider provider)
{
    private readonly ConcurrentDictionary<string, DiffAnalysis> _cache = new();

    public AiProvider Provider => provider;

    public async Task<DiffAnalysis> AnalyzeAsync(DiffDocument doc, string fileName, CancellationToken ct = default)
    {
        if (doc.Blocks.Count == 0)
            return new DiffAnalysis("Files are identical.", []);

        var prompt = DiffAnalysis.BuildPrompt(doc, fileName);
        var key = Hash($"{provider.Name}\n{provider.Model}\n{prompt}");
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var json = await provider.CompleteJsonAsync(prompt, DiffAnalysis.Schema, ct);
        var analysis = DiffAnalysis.Parse(json, doc.Blocks.Count);
        _cache[key] = analysis;
        return analysis;
    }

    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
