using VbNetSidecar.Extraction;
using VbNetSidecar.Model;
using Xunit;
using System.IO;

namespace VbNetSidecar.Tests;

/// <summary>
/// Tests for SIDE-10: multiple file paths produce a single merged JSON result.
/// Simulates what Program.cs does in production by calling VbExtractor.Extract per file
/// and merging the results.
/// </summary>
public class BatchModeTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // -----------------------------------------------------------------------
    // SIDE-10 — Multiple files return merged result
    // -----------------------------------------------------------------------

    [Fact]
    public void MultipleFilesReturnMergedResult()
    {
        // Simulate Program.cs batch merge
        var r1 = VbExtractor.Extract(FixturePath("simple_class.vb"));
        var r2 = VbExtractor.Extract(FixturePath("module_with_functions.vb"));

        var allNodes = r1.Nodes.Concat(r2.Nodes).ToList();
        var allEdges = r1.Edges.Concat(r2.Edges).ToList();

        // Both class and module should be present in merged set
        Assert.Contains(allNodes, n => n.Label == "SimpleClass");
        Assert.Contains(allNodes, n => n.Label == "TestModule");

        // Both file nodes should be present
        Assert.Contains(allNodes, n => n.Id == "simple_class");
        Assert.Contains(allNodes, n => n.Id == "module_with_functions");
    }

    [Fact]
    public void BatchModeNodesFromDifferentFiles()
    {
        var r1 = VbExtractor.Extract(FixturePath("simple_class.vb"));
        var r2 = VbExtractor.Extract(FixturePath("module_with_functions.vb"));

        var allNodes = r1.Nodes.Concat(r2.Nodes).ToList();

        // Nodes from different files have different source_file values
        var simpleClassNodes = allNodes.Where(n => n.SourceFile.Contains("simple_class")).ToList();
        var moduleNodes = allNodes.Where(n => n.SourceFile.Contains("module_with_functions")).ToList();

        Assert.NotEmpty(simpleClassNodes);
        Assert.NotEmpty(moduleNodes);

        // Source files are different
        Assert.NotEqual(
            simpleClassNodes.First().SourceFile,
            moduleNodes.First().SourceFile);

        // No duplicate node IDs across files
        // simple_class and module_with_functions use different file-stem prefixes
        var allIds = allNodes.Select(n => n.Id).ToList();
        Assert.Equal(allIds.Count, allIds.Distinct().Count());
    }
}
