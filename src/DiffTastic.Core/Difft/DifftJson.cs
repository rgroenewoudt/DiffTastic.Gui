using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiffTastic.Core.Difft;

public sealed record DifftResult(
    [property: JsonPropertyName("aligned_lines")] List<int?[]>? AlignedLines,
    [property: JsonPropertyName("chunks")] List<List<DifftLine>>? Chunks,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("status")] string? Status)
{
    public static DifftResult Parse(string json) =>
        JsonSerializer.Deserialize(json, DifftJsonContext.Default.DifftResult)
        ?? throw new InvalidDataException("Empty difft output.");
}

public sealed record DifftLine(
    [property: JsonPropertyName("lhs")] DifftSide? Lhs,
    [property: JsonPropertyName("rhs")] DifftSide? Rhs);

public sealed record DifftSide(
    [property: JsonPropertyName("line_number")] int LineNumber,
    [property: JsonPropertyName("changes")] List<DifftChange> Changes);

/// <summary>Start and End are UTF-8 byte offsets within the line.</summary>
public sealed record DifftChange(
    [property: JsonPropertyName("start")] int Start,
    [property: JsonPropertyName("end")] int End,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("highlight")] string Highlight);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DifftResult))]
internal sealed partial class DifftJsonContext : JsonSerializerContext;
