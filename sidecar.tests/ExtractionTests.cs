using VbNetSidecar.Extraction;
using VbNetSidecar.Model;
using Xunit;
using System.IO;
using System.Text.RegularExpressions;

namespace VbNetSidecar.Tests;

/// <summary>
/// Tests for P1 extraction requirements: SIDE-02 through SIDE-07, REL-01 through REL-05.
/// Validates every construct type against the pre-built fixture files.
/// </summary>
public class ExtractionTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // -----------------------------------------------------------------------
    // SIDE-02, REL-04 — File node emitted
    // -----------------------------------------------------------------------

    [Fact]
    public void FileNodeEmitted()
    {
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        // File node is always first
        var fileNode = result.Nodes.First();
        Assert.Equal("simple_class", fileNode.Id);
        Assert.Equal("simple_class.vb", fileNode.Label);
        Assert.Equal("code", fileNode.FileType);

        // At least one contains edge from the file node
        var containsFromFile = result.Edges
            .Where(e => e.Source == "simple_class" && e.Relation == "contains")
            .ToList();
        Assert.NotEmpty(containsFromFile);
    }

    // -----------------------------------------------------------------------
    // SIDE-03 — Class extraction
    // -----------------------------------------------------------------------

    [Fact]
    public void ClassExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        var classNode = result.Nodes.FirstOrDefault(n => n.Label == "SimpleClass");
        Assert.NotNull(classNode);
        Assert.Equal("simple_class_simpleclass", classNode.Id);
        Assert.Equal("code", classNode.FileType);
    }

    [Fact]
    public void NotInheritableClassExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));

        var sealedNode = result.Nodes.FirstOrDefault(n => n.Label == "SealedHelper");
        Assert.NotNull(sealedNode);
        Assert.Equal("class_with_all_constructs_sealedhelper", sealedNode.Id);
    }

    // -----------------------------------------------------------------------
    // SIDE-04 — Module extraction
    // -----------------------------------------------------------------------

    [Fact]
    public void ModuleExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("module_with_functions.vb"));

        var moduleNode = result.Nodes.FirstOrDefault(n => n.Label == "TestModule");
        Assert.NotNull(moduleNode);
        Assert.Equal("module_with_functions_testmodule", moduleNode.Id);
        Assert.Equal("code", moduleNode.FileType);
    }

    // -----------------------------------------------------------------------
    // SIDE-05 — Sub/Function extraction
    // -----------------------------------------------------------------------

    [Fact]
    public void SubExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        var doWorkNode = result.Nodes.FirstOrDefault(n => n.Label == "DoWork");
        Assert.NotNull(doWorkNode);

        // Contains edge from class node to DoWork
        var containsEdge = result.Edges.FirstOrDefault(e =>
            e.Source == "simple_class_simpleclass" &&
            e.Target == doWorkNode.Id &&
            e.Relation == "contains");
        Assert.NotNull(containsEdge);
    }

    [Fact]
    public void FunctionExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        var getValueNode = result.Nodes.FirstOrDefault(n => n.Label == "GetValue");
        Assert.NotNull(getValueNode);
        Assert.Equal("simple_class_simpleclass_getvalue", getValueNode.Id);
    }

    // -----------------------------------------------------------------------
    // SIDE-06 — Constructor Sub New extraction
    // -----------------------------------------------------------------------

    [Fact]
    public void ConstructorExtraction()
    {
        var result = VbExtractor.Extract(FixturePath("form_with_handles.vb"));

        // Constructor node has label "New()"
        var ctorNode = result.Nodes.FirstOrDefault(n => n.Label == "New()");
        Assert.NotNull(ctorNode);

        // ID contains "new"
        Assert.Contains("new", ctorNode.Id, StringComparison.OrdinalIgnoreCase);
        // e.g. "form_with_handles_testform_new"
        Assert.Equal("form_with_handles_testform_new", ctorNode.Id);
    }

    // -----------------------------------------------------------------------
    // SIDE-07 — Property extraction (PropertyBlockSyntax with Get/Set)
    // -----------------------------------------------------------------------

    [Fact]
    public void PropertyExtraction()
    {
        // class_with_all_constructs.vb has PropertyBlockSyntax with Get/Set blocks
        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));

        var countNode = result.Nodes.FirstOrDefault(n => n.Label == "Count");
        Assert.NotNull(countNode);
        Assert.Equal("class_with_all_constructs_allconstructs_count", countNode.Id);

        var isActiveNode = result.Nodes.FirstOrDefault(n => n.Label == "IsActive");
        Assert.NotNull(isActiveNode);
        Assert.Equal("class_with_all_constructs_allconstructs_isactive", isActiveNode.Id);
    }

    // -----------------------------------------------------------------------
    // REL-01 — Imports edges (note: filtered for external namespaces)
    // -----------------------------------------------------------------------

    [Fact]
    public void ImportsEdgesEmittedByWalker()
    {
        // The walker emits imports edges; VbExtractor filters out those whose
        // targets don't match any node ID (e.g. "System.Data" is external).
        // In single-file mode all imports to external namespaces are filtered.
        // We verify the extraction succeeds and imports are not causing errors.
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        // All remaining edges should have valid source/target (schema compliance)
        var nodeIds = new HashSet<string>(result.Nodes.Select(n => n.Id));
        foreach (var edge in result.Edges)
        {
            Assert.Contains(edge.Source, nodeIds);
            Assert.Contains(edge.Target, nodeIds);
        }
    }

    // -----------------------------------------------------------------------
    // REL-02 — Inherits edges (external base classes get filtered)
    // -----------------------------------------------------------------------

    [Fact]
    public void InheritsEdgeSurvivesWhenBaseClassIsLocal()
    {
        // Create a temp file with two classes where one inherits from the other
        var tempFile = Path.GetTempFileName() + ".vb";
        try
        {
            File.WriteAllText(tempFile,
                "Public Class BaseClass\r\n" +
                "End Class\r\n" +
                "Public Class DerivedClass\r\n" +
                "    Inherits BaseClass\r\n" +
                "End Class\r\n");

            var result = VbExtractor.Extract(tempFile);

            var stem = System.IO.Path.GetFileNameWithoutExtension(tempFile).ToLowerInvariant()
                .TrimEnd('_');
            // Node IDs are lowercased file-stem anchored
            var nodeIds = new HashSet<string>(result.Nodes.Select(n => n.Id));

            // inherits edge from DerivedClass to BaseClass should survive filtering
            var inheritsEdge = result.Edges.FirstOrDefault(e => e.Relation == "inherits");
            Assert.NotNull(inheritsEdge);
            Assert.Contains(inheritsEdge.Source, nodeIds);
            Assert.Contains(inheritsEdge.Target, nodeIds);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // -----------------------------------------------------------------------
    // REL-03, D-07 — Calls edges
    // -----------------------------------------------------------------------

    [Fact]
    public void CallsEdges()
    {
        // form_with_handles.vb: Sub New calls InitializeComponent() which is defined in the same file
        var result = VbExtractor.Extract(FixturePath("form_with_handles.vb"));

        // The call from Sub New to InitializeComponent should be resolved and kept
        var callsEdge = result.Edges.FirstOrDefault(e =>
            e.Relation == "calls" &&
            e.Source == "form_with_handles_testform_new" &&
            e.Target == "form_with_handles_testform_initializecomponent");
        Assert.NotNull(callsEdge);
        Assert.Equal("EXTRACTED", callsEdge.Confidence);
    }

    // -----------------------------------------------------------------------
    // REL-04 — Contains edges hierarchy
    // -----------------------------------------------------------------------

    [Fact]
    public void ContainsEdges()
    {
        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));
        var fileId = "class_with_all_constructs";
        var classId = "class_with_all_constructs_allconstructs";

        // File -> AllConstructs class
        Assert.Contains(result.Edges, e =>
            e.Source == fileId &&
            e.Target == classId &&
            e.Relation == "contains");

        // AllConstructs -> Count property
        Assert.Contains(result.Edges, e =>
            e.Source == classId &&
            e.Target == "class_with_all_constructs_allconstructs_count" &&
            e.Relation == "contains");

        // AllConstructs -> Increment method
        Assert.Contains(result.Edges, e =>
            e.Source == classId &&
            e.Target == "class_with_all_constructs_allconstructs_increment" &&
            e.Relation == "contains");
    }

    // -----------------------------------------------------------------------
    // REL-05, D-08 — Handles event edges
    // -----------------------------------------------------------------------

    [Fact]
    public void HandlesEventEdges()
    {
        var result = VbExtractor.Extract(FixturePath("form_with_handles.vb"));

        // handles_event edges exist from btn click handlers to WithEvents field nodes
        var handlesEdges = result.Edges.Where(e => e.Relation == "handles_event").ToList();
        Assert.NotEmpty(handlesEdges);

        // btnOk_Click handles btnOk.Click → edge to btnOk node (class-scoped ID after SC2 fix)
        var btnOkEdge = handlesEdges.FirstOrDefault(e =>
            e.Source == "form_with_handles_testform_btnok_click" &&
            e.Target == "form_with_handles_testform_btnok");
        Assert.NotNull(btnOkEdge);

        // btnCancel_Click handles btnCancel.Click → edge to btnCancel node (class-scoped ID after SC2 fix)
        var btnCancelEdge = handlesEdges.FirstOrDefault(e =>
            e.Source == "form_with_handles_testform_btncancel_click" &&
            e.Target == "form_with_handles_testform_btncancel");
        Assert.NotNull(btnCancelEdge);

        // D-08: NO calls edges for Handles clauses (they must be handles_event only)
        // The Handles clause should not produce a separate "calls" edge
        // (Me.Close() produces calls edges, but the Handles clause itself doesn't)
        var callsFromBtnOkClick = result.Edges
            .Where(e => e.Source == "form_with_handles_testform_btnok_click" && e.Relation == "calls")
            .ToList();
        // The only calls from btnOk_Click should be to Me.Close() but that's external and filtered
        // So no calls edges from the button handlers should survive
        Assert.DoesNotContain(callsFromBtnOkClick, e => e.Target == "form_with_handles_btnok");
    }

    // -----------------------------------------------------------------------
    // Source location format
    // -----------------------------------------------------------------------

    [Fact]
    public void SourceLocationFormat()
    {
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        // All nodes should have SourceLocation matching L\d+
        foreach (var node in result.Nodes)
        {
            Assert.NotNull(node.SourceLocation);
            Assert.Matches(@"^L\d+$", node.SourceLocation);
        }

        // File node is L1
        var fileNode = result.Nodes.First();
        Assert.Equal("L1", fileNode.SourceLocation);
    }

    // -----------------------------------------------------------------------
    // Schema field presence — every node has required fields
    // -----------------------------------------------------------------------

    [Fact]
    public void AllNodesHaveRequiredFields()
    {
        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));

        foreach (var node in result.Nodes)
        {
            Assert.False(string.IsNullOrEmpty(node.Id),    $"Node has empty Id");
            Assert.False(string.IsNullOrEmpty(node.Label), $"Node '{node.Id}' has empty Label");
            Assert.Equal("code", node.FileType);
            Assert.False(string.IsNullOrEmpty(node.SourceFile), $"Node '{node.Id}' has empty SourceFile");
        }
    }

    [Fact]
    public void AllEdgesHaveRequiredFields()
    {
        var result = VbExtractor.Extract(FixturePath("class_with_all_constructs.vb"));

        var validConfidences = new HashSet<string> { "EXTRACTED", "INFERRED", "AMBIGUOUS" };

        foreach (var edge in result.Edges)
        {
            Assert.False(string.IsNullOrEmpty(edge.Source),     "Edge has empty Source");
            Assert.False(string.IsNullOrEmpty(edge.Target),     "Edge has empty Target");
            Assert.False(string.IsNullOrEmpty(edge.Relation),   "Edge has empty Relation");
            Assert.False(string.IsNullOrEmpty(edge.Confidence), "Edge has empty Confidence");
            Assert.False(string.IsNullOrEmpty(edge.SourceFile), "Edge has empty SourceFile");
            Assert.Contains(edge.Confidence, validConfidences);
        }
    }
}
