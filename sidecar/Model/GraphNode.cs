using System.Text.Json.Serialization;

namespace VbNetSidecar.Model;

public sealed record GraphNode(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("file_type")] string FileType,
    [property: JsonPropertyName("source_file")] string SourceFile,
    [property: JsonPropertyName("source_location")] string? SourceLocation = null,
    [property: JsonPropertyName("attributes")] string[]? Attributes = null,
    [property: JsonPropertyName("is_shared")] bool? IsShared = null
);
