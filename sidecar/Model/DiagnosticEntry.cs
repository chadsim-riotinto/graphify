using System.Text.Json.Serialization;

namespace VbNetSidecar.Model;

public sealed record DiagnosticEntry(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("line")] int Line
);
