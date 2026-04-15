namespace VbNetSidecar.Model;

public sealed class ExtractionResult
{
    public List<GraphNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();
    public List<DiagnosticEntry> Diagnostics { get; } = new();
}
