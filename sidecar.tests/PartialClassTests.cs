using VbNetSidecar.Extraction;
using VbNetSidecar.Model;
using Xunit;
using System.IO;
using System.Linq;

namespace VbNetSidecar.Tests;

/// <summary>
/// Tests for MergePartialClasses() post-processing pass in VbExtractor.
/// Validates SC3: one merged class node per partial class pair after merge,
/// and SC2: handles_event edges from code file resolve against Designer WithEvents nodes.
/// </summary>
public class PartialClassTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void MergePartialClasses_MergesDesignerAndCodeNodes()
    {
        // Extract both files independently (simulating batch mode)
        var codeResult = VbExtractor.Extract(FixturePath("partial_class_code.vb"));
        var designerResult = VbExtractor.Extract(FixturePath("partial_class.Designer.vb"));

        // Combine into single result (as Program.cs does)
        var merged = new ExtractionResult();
        merged.Nodes.AddRange(codeResult.Nodes);
        merged.Nodes.AddRange(designerResult.Nodes);
        merged.Edges.AddRange(codeResult.Edges);
        merged.Edges.AddRange(designerResult.Edges);

        // Count class nodes with label "FrmTestPartial" BEFORE merge
        var preCount = merged.Nodes.Count(n => n.Label == "FrmTestPartial");
        Assert.Equal(2, preCount); // one from code file, one from Designer file

        VbExtractor.MergePartialClasses(merged);

        // After merge: exactly one class node with label "FrmTestPartial"
        var postCount = merged.Nodes.Count(n => n.Label == "FrmTestPartial");
        Assert.Equal(1, postCount);
    }

    [Fact]
    public void MergePartialClasses_DesignerChildrenGetCodeFileIdPrefix()
    {
        var codeResult = VbExtractor.Extract(FixturePath("partial_class_code.vb"));
        var designerResult = VbExtractor.Extract(FixturePath("partial_class.Designer.vb"));

        var merged = new ExtractionResult();
        merged.Nodes.AddRange(codeResult.Nodes);
        merged.Nodes.AddRange(designerResult.Nodes);
        merged.Edges.AddRange(codeResult.Edges);
        merged.Edges.AddRange(designerResult.Edges);

        VbExtractor.MergePartialClasses(merged);

        // The WithEvents btnSave field from Designer file should now have
        // code-file class prefix: partial_class_code_frmtestpartial_btnsave
        // (NOT partial_class_designer_frmtestpartial_btnsave)
        var btnSaveNode = merged.Nodes.FirstOrDefault(n => n.Label == "btnSave");
        Assert.NotNull(btnSaveNode);
        Assert.StartsWith("partial_class_code_frmtestpartial", btnSaveNode.Id);
    }

    [Fact]
    public void MergePartialClasses_HandlesEventEdgesResolveAfterMerge()
    {
        var codeResult = VbExtractor.Extract(FixturePath("partial_class_code.vb"));
        var designerResult = VbExtractor.Extract(FixturePath("partial_class.Designer.vb"));

        var merged = new ExtractionResult();
        merged.Nodes.AddRange(codeResult.Nodes);
        merged.Nodes.AddRange(designerResult.Nodes);
        merged.Edges.AddRange(codeResult.Edges);
        merged.Edges.AddRange(designerResult.Edges);

        VbExtractor.MergePartialClasses(merged);

        // handles_event edge from btnSave_Click to btnSave should survive FilterEdges
        var handlesEdges = merged.Edges.Where(e => e.Relation == "handles_event").ToList();
        Assert.NotEmpty(handlesEdges);

        // The edge target should match the remapped btnSave node ID
        var btnSaveNode = merged.Nodes.First(n => n.Label == "btnSave");
        var matchingEdge = handlesEdges.FirstOrDefault(e => e.Target == btnSaveNode.Id);
        Assert.NotNull(matchingEdge);
    }

    [Fact]
    public void MergePartialClasses_SkipsNonDesignerGeneratedFiles()
    {
        // Settings.Designer.vb has CompilerGenerated, NOT DesignerGenerated.
        // The merge should NOT affect it.
        // Simulate with a result that has no DesignerGenerated attribute.
        var result = new ExtractionResult();
        result.Nodes.Add(new GraphNode("settings_designer_my_mysettings", "MySettings", "code",
            "Settings.Designer.vb", "L1",
            new[] { "Global.System.Runtime.CompilerServices.CompilerGeneratedAttribute" }));
        result.Nodes.Add(new GraphNode("settings_designer", "Settings.Designer.vb", "code",
            "Settings.Designer.vb", "L1"));

        var nodeCountBefore = result.Nodes.Count;
        VbExtractor.MergePartialClasses(result);
        Assert.Equal(nodeCountBefore, result.Nodes.Count); // no change
    }

    [Fact]
    public void MergePartialClasses_PassesValidateExtraction()
    {
        var codeResult = VbExtractor.Extract(FixturePath("partial_class_code.vb"));
        var designerResult = VbExtractor.Extract(FixturePath("partial_class.Designer.vb"));

        var merged = new ExtractionResult();
        merged.Nodes.AddRange(codeResult.Nodes);
        merged.Nodes.AddRange(designerResult.Nodes);
        merged.Edges.AddRange(codeResult.Edges);
        merged.Edges.AddRange(designerResult.Edges);

        VbExtractor.MergePartialClasses(merged);

        // C# replica of validate_extraction: all edge sources and targets must be valid node IDs
        var nodeIds = new HashSet<string>(merged.Nodes.Select(n => n.Id));
        var badEdges = merged.Edges
            .Where(e => !nodeIds.Contains(e.Source) || !nodeIds.Contains(e.Target))
            .ToList();

        Assert.True(badEdges.Count == 0,
            $"validate_extraction would fail: {badEdges.Count} edges with invalid source/target:\n" +
            string.Join("\n", badEdges.Select(e => $"  {e.Source} -> {e.Target} ({e.Relation})")));
    }
}
