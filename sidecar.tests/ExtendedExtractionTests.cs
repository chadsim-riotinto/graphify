using VbNetSidecar.Extraction;
using VbNetSidecar.Model;
using Xunit;
using System.IO;
using System.Linq;

namespace VbNetSidecar.Tests;

/// <summary>
/// Tests for P2 extraction requirements: SIDE-08, SIDE-09, REL-06, REL-07, REL-08, REL-09.
/// These tests are written BEFORE the walker modifications (TDD RED phase).
/// They will fail until Plan 02 implements the walker changes.
/// </summary>
public class ExtendedExtractionTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // -----------------------------------------------------------------------
    // SIDE-08 — Namespace extraction
    // -----------------------------------------------------------------------

    [Fact]
    public void NamespaceBlock_EmitsNamespaceNode()
    {
        var result = VbExtractor.Extract(FixturePath("namespace_class.vb"));
        var nsNode = result.Nodes.FirstOrDefault(n => n.Label == "TestNamespace");
        Assert.NotNull(nsNode);
        Assert.Equal("namespace_class_testnamespace", nsNode.Id);
        Assert.Equal("code", nsNode.FileType);
    }

    [Fact]
    public void NamespaceBlock_ContainsEdgeFromFileToNamespace()
    {
        var result = VbExtractor.Extract(FixturePath("namespace_class.vb"));
        Assert.Contains(result.Edges, e =>
            e.Source == "namespace_class" &&
            e.Target == "namespace_class_testnamespace" &&
            e.Relation == "contains");
    }

    [Fact]
    public void NamespaceBlock_ClassIsChildOfNamespaceNotFile()
    {
        var result = VbExtractor.Extract(FixturePath("namespace_class.vb"));
        // Class should be child of namespace, not of file
        Assert.Contains(result.Edges, e =>
            e.Source == "namespace_class_testnamespace" &&
            e.Target == "namespace_class_testnamespace_namespacedclass" &&
            e.Relation == "contains");
        // Class should NOT have a direct contains edge from file node
        Assert.DoesNotContain(result.Edges, e =>
            e.Source == "namespace_class" &&
            e.Target.Contains("namespacedclass") &&
            e.Relation == "contains");
    }

    // -----------------------------------------------------------------------
    // SIDE-09 — Nested class extraction
    // -----------------------------------------------------------------------

    [Fact]
    public void NestedClass_InnerClassIsChildOfOuterClass()
    {
        var result = VbExtractor.Extract(FixturePath("nested_classes.vb"));
        var innerNode = result.Nodes.FirstOrDefault(n => n.Label == "InnerClass");
        Assert.NotNull(innerNode);
        Assert.Equal("nested_classes_outerclass_innerclass", innerNode.Id);

        // Contains edge from OuterClass to InnerClass
        Assert.Contains(result.Edges, e =>
            e.Source == "nested_classes_outerclass" &&
            e.Target == "nested_classes_outerclass_innerclass" &&
            e.Relation == "contains");
    }

    [Fact]
    public void NestedClass_InnerMethodIsChildOfInnerClass()
    {
        var result = VbExtractor.Extract(FixturePath("nested_classes.vb"));
        var innerMethodNode = result.Nodes.FirstOrDefault(n => n.Label == "InnerMethod");
        Assert.NotNull(innerMethodNode);
        Assert.Equal("nested_classes_outerclass_innerclass_innermethod", innerMethodNode.Id);
    }

    // -----------------------------------------------------------------------
    // REL-06 — Implements clause edges
    // -----------------------------------------------------------------------

    [Fact]
    public void ImplementsClause_EmitsImplementsEdge()
    {
        var result = VbExtractor.Extract(FixturePath("implements_class.vb"));
        var implementsEdges = result.Edges.Where(e => e.Relation == "implements").ToList();
        Assert.NotEmpty(implementsEdges);
        // At least one implements edge from the Worker class to the IWorker interface node
        // Target is the node ID (lowercased): implements_class_iworker
        Assert.Contains(implementsEdges, e =>
            e.Source == "implements_class_worker" &&
            e.Target == "implements_class_iworker");
    }

    // -----------------------------------------------------------------------
    // REL-07 — Friend WithEvents declares_event_source edges
    // -----------------------------------------------------------------------

    [Fact]
    public void FriendWithEvents_EmitsDeclaresEventSourceEdge()
    {
        // form_with_handles.vb has "Friend WithEvents btnOk" and "Friend WithEvents btnCancel"
        var result = VbExtractor.Extract(FixturePath("form_with_handles.vb"));
        var desEdges = result.Edges.Where(e => e.Relation == "declares_event_source").ToList();
        Assert.NotEmpty(desEdges);
        // btnOk should have declares_event_source (not contains) — class-scoped ID after SC2 fix
        Assert.Contains(desEdges, e =>
            e.Target == "form_with_handles_testform_btnok");
    }

    [Fact]
    public void FriendWithEvents_NoContainsEdgeForWithEventsFields()
    {
        var result = VbExtractor.Extract(FixturePath("form_with_handles.vb"));
        // Friend WithEvents fields should NOT have "contains" edges -- they should have "declares_event_source"
        // Use class-scoped ID after SC2 fix
        var containsToBtn = result.Edges.Where(e =>
            e.Target == "form_with_handles_testform_btnok" &&
            e.Relation == "contains").ToList();
        Assert.Empty(containsToBtn);
    }

    // -----------------------------------------------------------------------
    // REL-08 — Attribute metadata on nodes
    // -----------------------------------------------------------------------

    [Fact]
    public void AttributeMetadata_ClassHasAttributes()
    {
        var result = VbExtractor.Extract(FixturePath("attributed_class.vb"));
        var classNode = result.Nodes.FirstOrDefault(n => n.Label == "AttributedClass");
        Assert.NotNull(classNode);
        Assert.NotNull(classNode.Attributes);
        Assert.Contains("Serializable", classNode.Attributes);
    }

    [Fact]
    public void AttributeMetadata_MethodHasAttributes()
    {
        var result = VbExtractor.Extract(FixturePath("attributed_class.vb"));
        var oldMethodNode = result.Nodes.FirstOrDefault(n => n.Label == "OldMethod");
        Assert.NotNull(oldMethodNode);
        Assert.NotNull(oldMethodNode.Attributes);
        Assert.Contains("Obsolete", oldMethodNode.Attributes);

        // NewMethod has no attributes
        var newMethodNode = result.Nodes.FirstOrDefault(n => n.Label == "NewMethod");
        Assert.NotNull(newMethodNode);
        Assert.Null(newMethodNode.Attributes);
    }

    // -----------------------------------------------------------------------
    // REL-09 — Shared modifier flag
    // -----------------------------------------------------------------------

    [Fact]
    public void SharedModifier_MethodHasIsSharedTrue()
    {
        var result = VbExtractor.Extract(FixturePath("shared_members.vb"));
        var sharedMethodNode = result.Nodes.FirstOrDefault(n => n.Label == "SharedMethod");
        Assert.NotNull(sharedMethodNode);
        Assert.True(sharedMethodNode.IsShared);

        var sharedFuncNode = result.Nodes.FirstOrDefault(n => n.Label == "SharedFunction");
        Assert.NotNull(sharedFuncNode);
        Assert.True(sharedFuncNode.IsShared);
    }

    [Fact]
    public void SharedModifier_PropertyHasIsSharedTrue()
    {
        var result = VbExtractor.Extract(FixturePath("shared_members.vb"));
        var sharedPropNode = result.Nodes.FirstOrDefault(n => n.Label == "SharedProp");
        Assert.NotNull(sharedPropNode);
        Assert.True(sharedPropNode.IsShared);
    }

    [Fact]
    public void SharedModifier_InstanceMethodHasNoIsShared()
    {
        var result = VbExtractor.Extract(FixturePath("shared_members.vb"));
        var instanceMethodNode = result.Nodes.FirstOrDefault(n => n.Label == "InstanceMethod");
        Assert.NotNull(instanceMethodNode);
        Assert.Null(instanceMethodNode.IsShared);
    }
}
