using System.Text.Json;
using System.Text.Json.Serialization;
using VbNetSidecar.Extraction;
using VbNetSidecar.Model;

// --- Argument validation (T-01-09 threat mitigation) ---
if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: VbNetSidecar <file1.vb> [file2.vb] ...");
    return 1;
}

// --- Batch extraction ---
var allNodes = new List<GraphNode>();
var allEdges = new List<GraphEdge>();
var allDiagnostics = new List<DiagnosticEntry>();

foreach (var path in args)
{
    // Validate each path is a .vb file (T-01-08 threat mitigation)
    if (!path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"Skipping non-.vb file: {path}");
        continue;
    }

    try
    {
        var result = VbExtractor.Extract(path);
        allNodes.AddRange(result.Nodes);
        allEdges.AddRange(result.Edges);
        allDiagnostics.AddRange(result.Diagnostics);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error processing {path}: {ex.Message}");
        allDiagnostics.Add(new DiagnosticEntry(path, "Error", ex.Message, 0));
    }
}

// --- Partial class merging (SC3) ---
// Re-root Designer.vb nodes under their code-file class counterparts.
// Must run after all files are extracted so both Designer and code class nodes exist.
var batchResult = new ExtractionResult();
batchResult.Nodes.AddRange(allNodes);
batchResult.Edges.AddRange(allEdges);
VbExtractor.MergePartialClasses(batchResult);

// Replace batch lists with merged result
allNodes.Clear();
allNodes.AddRange(batchResult.Nodes);
allEdges.Clear();
allEdges.AddRange(batchResult.Edges);

// --- JSON serialization to stdout (T-01-07 threat mitigation) ---
// GraphNode/GraphEdge/DiagnosticEntry records use [JsonPropertyName] attributes
// producing snake_case field names: id, file_type, source_file, source_location.
// The anonymous object keys (nodes, edges, diagnostics) are already lowercase.
var output = new
{
    nodes = allNodes,
    edges = allEdges,
    diagnostics = allDiagnostics
};

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// CRITICAL: exactly ONE Console.WriteLine — the final JSON output.
// ALL other output uses Console.Error.WriteLine so stdout is pure JSON.
Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
return 0;
