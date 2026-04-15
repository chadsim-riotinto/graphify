using System.Text.Json.Serialization;

namespace VbNetSidecar.Model;

public sealed record GraphEdge(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("relation")] string Relation,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("source_file")] string SourceFile
);
