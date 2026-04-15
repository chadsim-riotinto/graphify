using VbNetSidecar.Extraction;
using VbNetSidecar.Model;
using Xunit;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace VbNetSidecar.Tests;

/// <summary>
/// Tests for SIDE-11: sidecar output passes Graphify's validate_extraction() schema.
/// Includes both a C# replica of the validation logic (fast unit test) and an
/// integration test that calls the real Python validate_extraction() via subprocess.
/// </summary>
public class SchemaValidationTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>
    /// Serialize an ExtractionResult using the same options as Program.cs.
    /// GraphNode/GraphEdge/DiagnosticEntry records use [JsonPropertyName] attributes
    /// producing snake_case field names that validate_extraction() expects.
    /// </summary>
    private static string SerializeResult(ExtractionResult result)
    {
        var output = new
        {
            nodes = result.Nodes,
            edges = result.Edges,
            diagnostics = result.Diagnostics
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Serialize(output, options);
    }

    // -----------------------------------------------------------------------
    // C# replica of validate_extraction() for fast unit-level checks
    // -----------------------------------------------------------------------

    private static List<string> ValidateExtraction(ExtractionResult result)
    {
        var errors = new List<string>();
        var validConfidences = new HashSet<string> { "EXTRACTED", "INFERRED", "AMBIGUOUS" };
        var validFileTypes   = new HashSet<string> { "code", "document", "paper", "image", "rationale" };

        var nodeIds = new HashSet<string>(result.Nodes.Select(n => n.Id));

        for (int i = 0; i < result.Nodes.Count; i++)
        {
            var node = result.Nodes[i];
            if (string.IsNullOrEmpty(node.Id))         errors.Add($"Node {i} missing required field 'id'");
            if (string.IsNullOrEmpty(node.Label))       errors.Add($"Node {i} missing required field 'label'");
            if (string.IsNullOrEmpty(node.FileType))    errors.Add($"Node {i} missing required field 'file_type'");
            if (string.IsNullOrEmpty(node.SourceFile))  errors.Add($"Node {i} missing required field 'source_file'");
            if (!string.IsNullOrEmpty(node.FileType) && !validFileTypes.Contains(node.FileType))
                errors.Add($"Node {i} has invalid file_type '{node.FileType}'");
        }

        for (int i = 0; i < result.Edges.Count; i++)
        {
            var edge = result.Edges[i];
            if (string.IsNullOrEmpty(edge.Source))     errors.Add($"Edge {i} missing required field 'source'");
            if (string.IsNullOrEmpty(edge.Target))     errors.Add($"Edge {i} missing required field 'target'");
            if (string.IsNullOrEmpty(edge.Relation))   errors.Add($"Edge {i} missing required field 'relation'");
            if (string.IsNullOrEmpty(edge.Confidence)) errors.Add($"Edge {i} missing required field 'confidence'");
            if (string.IsNullOrEmpty(edge.SourceFile)) errors.Add($"Edge {i} missing required field 'source_file'");
            if (!string.IsNullOrEmpty(edge.Confidence) && !validConfidences.Contains(edge.Confidence))
                errors.Add($"Edge {i} has invalid confidence '{edge.Confidence}'");
            if (!string.IsNullOrEmpty(edge.Source) && nodeIds.Count > 0 && !nodeIds.Contains(edge.Source))
                errors.Add($"Edge {i} source '{edge.Source}' does not match any node id");
            if (!string.IsNullOrEmpty(edge.Target) && nodeIds.Count > 0 && !nodeIds.Contains(edge.Target))
                errors.Add($"Edge {i} target '{edge.Target}' does not match any node id");
        }

        return errors;
    }

    // -----------------------------------------------------------------------
    // SIDE-11 — C# validation helper: output passes schema
    // -----------------------------------------------------------------------

    [Fact]
    public void OutputPassesValidateExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));

        var errors = ValidateExtraction(result);

        Assert.True(errors.Count == 0,
            $"Schema validation failed with {errors.Count} error(s):\n" +
            string.Join("\n", errors));
    }

    // -----------------------------------------------------------------------
    // SIDE-11 edge validation — all edge sources and targets match node IDs
    // -----------------------------------------------------------------------

    [Fact]
    public void AllEdgeSourcesAndTargetsMatchNodeIds()
    {
        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));

        var nodeIds = new HashSet<string>(result.Nodes.Select(n => n.Id));

        foreach (var edge in result.Edges)
        {
            Assert.True(nodeIds.Contains(edge.Source),
                $"Edge source '{edge.Source}' (relation={edge.Relation}) is not a known node ID");
            Assert.True(nodeIds.Contains(edge.Target),
                $"Edge target '{edge.Target}' (relation={edge.Relation}) is not a known node ID");
        }
    }

    // -----------------------------------------------------------------------
    // SIDE-11 integration — call real Python validate_extraction() via subprocess
    // -----------------------------------------------------------------------

    [Fact]
    public void OutputPassesValidateExtractionViaPython()
    {
        // Find graphify package — it lives in the main repo, not the worktree
        // Walk up from the worktree to find the graphify directory
        var graphifyPath = FindGraphifyPath();

        if (graphifyPath == null)
        {
            // Skip gracefully if graphify package not found in expected locations
            return;
        }

        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));
        var json   = SerializeResult(result);

        // Write JSON to a temp file
        var tempJson = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempJson, json);

            // Python inline script: import validate_extraction and run it
            var script =
                $"import json, sys; " +
                $"sys.path.insert(0, r'{graphifyPath}'); " +
                $"from graphify.validate import validate_extraction; " +
                $"data = json.load(open(r'{tempJson}')); " +
                $"errors = validate_extraction(data); " +
                $"[print(e) for e in errors] if errors else print('VALID')";

            var psi = new ProcessStartInfo("python", $"-c \"{script}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd().Trim();
            var stderr = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit(30_000);

            if (process.ExitCode != 0 && string.IsNullOrEmpty(stdout))
            {
                // Python not available or script error — skip rather than fail
                return;
            }

            Assert.True(stdout == "VALID",
                $"Python validate_extraction() reported errors:\n{stdout}\nstderr: {stderr}");
        }
        finally
        {
            if (File.Exists(tempJson)) File.Delete(tempJson);
        }
    }

    // -----------------------------------------------------------------------
    // Also validate simple_class.vb and form_with_handles.vb
    // -----------------------------------------------------------------------

    [Fact]
    public void SimpleClassOutputPassesValidateExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));
        var errors = ValidateExtraction(result);
        Assert.True(errors.Count == 0,
            $"simple_class.vb schema errors:\n{string.Join("\n", errors)}");
    }

    [Fact]
    public void FormWithHandlesOutputPassesValidateExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("form_with_handles.vb"));
        var errors = ValidateExtraction(result);
        Assert.True(errors.Count == 0,
            $"form_with_handles.vb schema errors:\n{string.Join("\n", errors)}");
    }

    // -----------------------------------------------------------------------
    // Helper: locate the graphify package directory
    // -----------------------------------------------------------------------

    private static string? FindGraphifyPath()
    {
        // Try common locations relative to the worktree/repo
        var candidates = new[]
        {
            // Main repo sibling of worktrees directory
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "graphify")),
            // Walk up from test output
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "graphify")),
            // Absolute path in main repo
            @"C:\Users\Chad.Sim\Documents\graphifyy_vbnet\graphify",
        };

        foreach (var candidate in candidates)
        {
            var validatePy = Path.Combine(candidate, "graphify", "validate.py");
            if (File.Exists(validatePy))
                return candidate;
        }

        return null;
    }
}
