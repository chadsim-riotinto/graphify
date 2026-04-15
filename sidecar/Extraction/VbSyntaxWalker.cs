using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VbNetSidecar.Model;

namespace VbNetSidecar.Extraction;

/// <summary>
/// VisualBasicSyntaxWalker that traverses the Roslyn AST and emits GraphNode and GraphEdge
/// objects for every P1 construct: classes, modules, methods, constructors, properties,
/// WithEvents fields, imports, inherits, calls, and handles_event relations.
/// Extended in Phase 2 for: namespace blocks (SIDE-08), implements clauses (REL-06),
/// declares_event_source edges for Friend WithEvents (REL-07), attribute metadata (REL-08),
/// and Shared modifier flags (REL-09).
/// </summary>
internal sealed class VbSyntaxWalker : VisualBasicSyntaxWalker
{
    private readonly Stack<string> _parentIdStack = new();
    private readonly string _fileId;
    private readonly string _sourcePath;
    private readonly SyntaxTree _tree;

    /// <summary>
    /// Tracks emitted namespace node IDs to prevent duplicate namespace nodes.
    /// Files with repeated Namespace blocks (e.g. Settings.Designer.vb) only emit
    /// one namespace node per (file, namespaceName) pair. See Pitfall 2 in 02-RESEARCH.md.
    /// </summary>
    private readonly HashSet<string> _emittedNodeIds = new(StringComparer.Ordinal);

    public List<GraphNode> Nodes { get; } = new();
    public List<GraphEdge> Edges { get; } = new();

    /// <summary>
    /// Initializes the walker.
    /// </summary>
    /// <param name="fileId">File stem ID from NodeIdBuilder.GetFileStemId(), e.g. "dbclasses"</param>
    /// <param name="sourcePath">Original file path for source_file fields, e.g. "Source/CPR/DBClasses.vb"</param>
    /// <param name="tree">The parsed Roslyn syntax tree (needed for GetLineSpan())</param>
    public VbSyntaxWalker(string fileId, string sourcePath, SyntaxTree tree)
    {
        _fileId = fileId;
        _sourcePath = sourcePath;
        _tree = tree;
        // File node is emitted by VbExtractor before walking; push it as the root parent
        // so top-level class/module nodes get contains edges from the file node.
        _parentIdStack.Push(fileId);
    }

    // -----------------------------------------------------------------------
    // SIDE-08, REL-04 — Namespace block declarations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits NamespaceBlock nodes. Emits a namespace node and pushes it onto the
    /// parent stack so all enclosed classes/modules become children of the namespace.
    /// Deduplicates: files with repeated Namespace blocks (e.g. Settings.Designer.vb)
    /// only emit one namespace node per (file, namespaceName) pair.
    /// MUST call base.VisitNamespaceBlock to recurse into enclosed types.
    /// </summary>
    public override void VisitNamespaceBlock(NamespaceBlockSyntax node)
    {
        var nsName = node.NamespaceStatement.Name.ToString();
        var id = NodeIdBuilder.MakeId(_parentIdStack.Peek(), nsName);
        var line = GetLine(node.NamespaceStatement);

        // Dedup: only emit node + edge if not already emitted for this file
        if (_emittedNodeIds.Add(id))
        {
            Nodes.Add(new GraphNode(id, nsName, "code", _sourcePath, $"L{line}"));
            Edges.Add(new GraphEdge(_parentIdStack.Peek(), id, "contains", "EXTRACTED", _sourcePath));
        }

        // INVARIANT (WR-04): Push is unconditional — even for deduped namespace blocks,
        // children must see the namespace as their parent. This is correct for repeated
        // blocks with the same name (e.g. Settings.Designer.vb) and for the alternating-
        // namespace pattern (Namespace A / Namespace B / Namespace A). The push/pop pair
        // is always balanced regardless of whether the node was emitted.
        _parentIdStack.Push(id);
        base.VisitNamespaceBlock(node); // recurse — visits enclosed classes/modules
        _parentIdStack.Pop();
    }

    // -----------------------------------------------------------------------
    // SIDE-03, REL-04 — Class declarations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits ClassBlock nodes: Class, NotInheritable Class, Partial Class.
    /// Emits a node and a contains edge from the current parent.
    /// Extracts attribute metadata (REL-08).
    /// MUST call base.VisitClassBlock to recurse into child methods/properties.
    /// </summary>
    public override void VisitClassBlock(ClassBlockSyntax node)
    {
        var name = node.ClassStatement.Identifier.ValueText;
        var id   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), name);
        var line = GetLine(node.ClassStatement);
        var attrs = GetAttributeNames(node);

        Nodes.Add(new GraphNode(id, name, "code", _sourcePath, $"L{line}")
            with { Attributes = attrs.Length > 0 ? attrs : null });
        Edges.Add(new GraphEdge(_parentIdStack.Peek(), id, "contains", "EXTRACTED", _sourcePath));

        _parentIdStack.Push(id);
        base.VisitClassBlock(node); // recurse — visits child methods, properties, constructors
        _parentIdStack.Pop();
    }

    // -----------------------------------------------------------------------
    // REL-06 support — Interface declarations (needed for local implements resolution)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits InterfaceBlock nodes. Emits a node and a contains edge from the current parent.
    /// Required so that locally-defined interfaces (e.g. IWorker in implements_class.vb)
    /// are present as node IDs and implements edges to them are kept by FilterEdges.
    /// MUST call base.VisitInterfaceBlock to recurse into child method declarations.
    /// </summary>
    public override void VisitInterfaceBlock(InterfaceBlockSyntax node)
    {
        var name = node.InterfaceStatement.Identifier.ValueText;
        var id   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), name);
        var line = GetLine(node.InterfaceStatement);

        Nodes.Add(new GraphNode(id, name, "code", _sourcePath, $"L{line}"));
        Edges.Add(new GraphEdge(_parentIdStack.Peek(), id, "contains", "EXTRACTED", _sourcePath));

        _parentIdStack.Push(id);
        base.VisitInterfaceBlock(node); // recurse
        _parentIdStack.Pop();
    }

    // -----------------------------------------------------------------------
    // SIDE-04, REL-04 — Module declarations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits ModuleBlock nodes.
    /// Emits a node and a contains edge from the current parent.
    /// MUST call base.VisitModuleBlock to recurse into child methods/properties.
    /// </summary>
    public override void VisitModuleBlock(ModuleBlockSyntax node)
    {
        var name = node.ModuleStatement.Identifier.ValueText;
        var id   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), name);
        var line = GetLine(node.ModuleStatement);

        Nodes.Add(new GraphNode(id, name, "code", _sourcePath, $"L{line}"));
        Edges.Add(new GraphEdge(_parentIdStack.Peek(), id, "contains", "EXTRACTED", _sourcePath));

        _parentIdStack.Push(id);
        base.VisitModuleBlock(node); // recurse
        _parentIdStack.Pop();
    }

    // -----------------------------------------------------------------------
    // SIDE-05, REL-03, REL-04, REL-05, REL-08, REL-09 — Sub/Function method declarations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits MethodBlock nodes (Sub and Function, excluding Sub New).
    /// Emits a node, a contains edge, and handles_event edges for Handles clauses.
    /// Extracts attribute metadata (REL-08) and Shared modifier flag (REL-09).
    /// MUST call base.VisitMethodBlock to capture InvocationExpression children.
    /// </summary>
    public override void VisitMethodBlock(MethodBlockSyntax node)
    {
        var name = node.SubOrFunctionStatement.Identifier.ValueText;
        var id   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), name);
        var line = GetLine(node.SubOrFunctionStatement);
        var isShared = node.SubOrFunctionStatement.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword));
        var attrs = GetAttributeNames(node);

        // WR-03: VB.NET allows method overloading (same name, different parameters).
        // Without dedup, overloaded methods produce identical node IDs and the second
        // overload is silently dropped by MergePartialClasses Step 5 dedup.
        // Append a numeric suffix to make each overload's ID unique.
        var uniqueId = id;
        int suffix = 2;
        while (!_emittedNodeIds.Add(uniqueId))
            uniqueId = $"{id}_{suffix++}";

        Nodes.Add(new GraphNode(uniqueId, name, "code", _sourcePath, $"L{line}")
            with { IsShared = isShared ? true : null,
                   Attributes = attrs.Length > 0 ? attrs : null });
        Edges.Add(new GraphEdge(_parentIdStack.Peek(), uniqueId, "contains", "EXTRACTED", _sourcePath));

        // REL-05 / D-08: Handles clauses emit handles_event edges.
        // Target is the WithEvents field node ID: NodeIdBuilder.MakeId(_fileId, containerName).
        // The synthetic WithEvents field node is emitted by VisitFieldDeclaration.
        // If the field node wasn't emitted (e.g. declared in another file), VbExtractor's
        // edge filter will drop the edge during the post-walk validation pass.
        var stmt = node.SubOrFunctionStatement;
        if (stmt.HandlesClause != null)
        {
            foreach (var item in stmt.HandlesClause.Events)
            {
                // item.EventContainer gives the object (e.g. "btnOk")
                // item.EventMember gives the event name (e.g. "Click")
                var containerName = GetHandlesContainerName(item);
                if (containerName != null)
                {
                    var targetId = NodeIdBuilder.MakeId(_parentIdStack.Peek(), containerName);
                    Edges.Add(new GraphEdge(uniqueId, targetId, "handles_event", "EXTRACTED", _sourcePath));
                }
            }
        }

        _parentIdStack.Push(uniqueId);
        base.VisitMethodBlock(node); // recurse — captures invocations inside the body
        _parentIdStack.Pop();
    }

    // -----------------------------------------------------------------------
    // SIDE-06, REL-09 — Constructor Sub New
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits ConstructorBlock (Sub New). CRITICAL: separate from MethodBlockSyntax.
    /// Emits a node with label "New()" and a contains edge from the current parent.
    /// Detects Shared modifier (REL-09 — Shared Sub New() static initializer pattern).
    /// MUST call base.VisitConstructorBlock to recurse into body (captures calls).
    /// </summary>
    public override void VisitConstructorBlock(ConstructorBlockSyntax node)
    {
        var id   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), "New");
        var line = GetLine(node.SubNewStatement);
        var isShared = node.SubNewStatement.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword));

        // WR-01 (iter 2): VB.NET supports constructor overloading (Sub New with different
        // parameter lists). Without dedup, overloaded constructors produce identical node IDs
        // and the second overload is silently dropped. Same pattern as VisitMethodBlock WR-03 fix.
        var uniqueId = id;
        int suffix = 2;
        while (!_emittedNodeIds.Add(uniqueId))
            uniqueId = $"{id}_{suffix++}";

        Nodes.Add(new GraphNode(uniqueId, "New()", "code", _sourcePath, $"L{line}")
            with { IsShared = isShared ? true : null });
        Edges.Add(new GraphEdge(_parentIdStack.Peek(), uniqueId, "contains", "EXTRACTED", _sourcePath));

        _parentIdStack.Push(uniqueId);
        base.VisitConstructorBlock(node); // recurse — captures InitializeComponent() calls
        _parentIdStack.Pop();
    }

    // -----------------------------------------------------------------------
    // SIDE-07, REL-04, REL-08, REL-09 — Property declarations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits PropertyBlock nodes (Get/Set property blocks).
    /// Emits a node and a contains edge from the current parent.
    /// Extracts attribute metadata (REL-08) and Shared modifier flag (REL-09).
    /// MUST call base.VisitPropertyBlock to recurse (handles auto-property bodies).
    /// </summary>
    public override void VisitPropertyBlock(PropertyBlockSyntax node)
    {
        var name = node.PropertyStatement.Identifier.ValueText;
        var id   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), name);
        var line = GetLine(node.PropertyStatement);
        var isShared = node.PropertyStatement.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword));
        var attrs = GetAttributeNames(node);

        Nodes.Add(new GraphNode(id, name, "code", _sourcePath, $"L{line}")
            with { IsShared = isShared ? true : null,
                   Attributes = attrs.Length > 0 ? attrs : null });
        Edges.Add(new GraphEdge(_parentIdStack.Peek(), id, "contains", "EXTRACTED", _sourcePath));

        _parentIdStack.Push(id);
        base.VisitPropertyBlock(node); // recurse
        _parentIdStack.Pop();
    }

    // -----------------------------------------------------------------------
    // REL-09, REL-08 — Auto-implemented property declarations
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits standalone PropertyStatement nodes (auto-implemented properties without
    /// explicit Get/Set blocks). Skips nodes whose Parent is PropertyBlockSyntax
    /// (those are handled by VisitPropertyBlock). Needed for Shared detection on
    /// auto-properties like "Public Shared ReadOnly Property X As Integer".
    /// </summary>
    public override void VisitPropertyStatement(PropertyStatementSyntax node)
    {
        // Skip if this is the header of a PropertyBlockSyntax (already handled)
        if (node.Parent is PropertyBlockSyntax)
        {
            base.VisitPropertyStatement(node);
            return;
        }

        var name = node.Identifier.ValueText;
        var id   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), name);
        var line = GetLine(node);
        var isShared = node.Modifiers.Any(m => m.IsKind(SyntaxKind.SharedKeyword));
        var attrs = ExtractAttrNames(node.AttributeLists);

        Nodes.Add(new GraphNode(id, name, "code", _sourcePath, $"L{line}")
            with { IsShared = isShared ? true : null,
                   Attributes = attrs.Length > 0 ? attrs : null });
        Edges.Add(new GraphEdge(_parentIdStack.Peek(), id, "contains", "EXTRACTED", _sourcePath));

        base.VisitPropertyStatement(node);
    }

    // -----------------------------------------------------------------------
    // REL-03, D-07 — Method invocations (calls edges)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits InvocationExpression nodes to emit calls edges.
    /// The raw method name is used as the target; VbExtractor's filter pass resolves
    /// it to a node ID or drops the edge (validate_extraction compliance).
    /// MUST call base.VisitInvocationExpression to continue descent.
    /// </summary>
    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Only emit calls edges from within a method/constructor context (stack > 1 means
        // we're inside something beyond the root file node).
        if (_parentIdStack.Count > 1)
        {
            var methodName = node.Expression switch
            {
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                IdentifierNameSyntax id          => id.Identifier.ValueText,
                _                                => null
            };

            if (methodName != null)
            {
                // Raw method name target — resolved/filtered in VbExtractor post-walk pass.
                Edges.Add(new GraphEdge(_parentIdStack.Peek(), methodName, "calls", "EXTRACTED", _sourcePath));
            }
        }

        base.VisitInvocationExpression(node); // continue descent
    }

    // -----------------------------------------------------------------------
    // REL-01 — Imports statements
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits ImportsStatement nodes to emit imports edges from the file node.
    /// Import targets are namespace strings (e.g. "System.Data"); VbExtractor's
    /// filter pass drops them if they don't match a node ID in the result.
    /// Does NOT call base — no meaningful sub-nodes to visit for graph extraction.
    /// </summary>
    public override void VisitImportsStatement(ImportsStatementSyntax node)
    {
        foreach (var clause in node.ImportsClauses)
        {
            Edges.Add(new GraphEdge(_fileId, clause.ToString(), "imports", "EXTRACTED", _sourcePath));
        }
        // Intentionally no base.VisitImportsStatement — no graph-relevant children
    }

    // -----------------------------------------------------------------------
    // REL-02 — Inherits statements
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits InheritsStatement nodes to emit inherits edges from the current class.
    /// Base type strings (e.g. "System.Windows.Forms.Form") are used as targets;
    /// VbExtractor's filter pass drops them if they don't match a node ID in the result.
    /// Does NOT call base — no meaningful sub-nodes to visit for graph extraction.
    /// </summary>
    public override void VisitInheritsStatement(InheritsStatementSyntax node)
    {
        foreach (var type in node.Types)
        {
            Edges.Add(new GraphEdge(_parentIdStack.Peek(), type.ToString(), "inherits", "EXTRACTED", _sourcePath));
        }
        // Intentionally no base.VisitInheritsStatement — no graph-relevant children
    }

    // -----------------------------------------------------------------------
    // REL-06 — Implements statement (class implements interface)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits ImplementsStatement nodes to emit implements edges from the current class.
    /// CRITICAL: Do NOT use classBlock.Members.OfType&lt;ImplementsStatementSyntax&gt;() --
    /// it returns zero results. Use this walker override instead (verified by live probe).
    /// Does NOT call base -- no graph-relevant children.
    /// </summary>
    public override void VisitImplementsStatement(ImplementsStatementSyntax node)
    {
        foreach (var type in node.Types)
        {
            Edges.Add(new GraphEdge(_parentIdStack.Peek(), type.ToString(), "implements", "EXTRACTED", _sourcePath));
        }
        // Intentionally no base.VisitImplementsStatement — no graph-relevant children
    }

    // -----------------------------------------------------------------------
    // REL-05, REL-07 — WithEvents field declarations (synthetic nodes for Handles targets)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Visits FieldDeclaration nodes. For fields with the WithEvents modifier,
    /// emits a synthetic node so that handles_event edges can resolve their targets
    /// to a valid node ID rather than being dropped by the filter pass.
    /// For Friend WithEvents fields (REL-07), emits "declares_event_source" edge
    /// instead of "contains". Non-Friend WithEvents fields retain "contains".
    /// Does NOT push onto the parent stack (fields don't contain child symbols).
    /// </summary>
    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        // Only synthetic nodes for WithEvents fields (REL-05 support)
        if (node.Modifiers.Any(m => m.IsKind(SyntaxKind.WithEventsKeyword)))
        {
            bool hasFriend = node.Modifiers.Any(m => m.IsKind(SyntaxKind.FriendKeyword));
            var relation = hasFriend ? "declares_event_source" : "contains";

            foreach (var declarator in node.Declarators)
            {
                foreach (var name in declarator.Names)
                {
                    var fieldName = name.Identifier.ValueText;
                    var fieldId   = NodeIdBuilder.MakeId(_parentIdStack.Peek(), fieldName);
                    var line      = GetLine(name);
                    var attrs     = GetAttributeNames(node);

                    Nodes.Add(new GraphNode(fieldId, fieldName, "code", _sourcePath, $"L{line}")
                        with { Attributes = attrs.Length > 0 ? attrs : null });
                    Edges.Add(new GraphEdge(_parentIdStack.Peek(), fieldId, relation, "EXTRACTED", _sourcePath));
                }
            }
        }

        base.VisitFieldDeclaration(node); // continue descent (unlikely to have meaningful children)
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns 1-indexed line number for the given syntax node.
    /// </summary>
    private int GetLine(SyntaxNode node) =>
        _tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;

    /// <summary>
    /// Extracts the container name (WithEvents field name) from a HandlesClauseItemSyntax.
    /// Returns null if the container cannot be determined.
    /// e.g. "btnOk.Click" → "btnOk"
    /// </summary>
    private static string? GetHandlesContainerName(HandlesClauseItemSyntax item)
    {
        return item.EventContainer switch
        {
            WithEventsEventContainerSyntax we => we.Identifier.ValueText,
            KeywordEventContainerSyntax    _  => null, // "Me" or "MyBase" — skip
            _                                 => null
        };
    }

    /// <summary>
    /// Extracts attribute names from a syntax node. Returns the attribute type names
    /// (e.g. "Serializable", "Obsolete", "Global.System.Runtime.CompilerServices.CompilerGeneratedAttribute")
    /// using a.Name.ToString() (NOT a.ToString() which includes arguments).
    /// Returns empty array if node has no attributes.
    /// </summary>
    private static string[] GetAttributeNames(SyntaxNode node)
    {
        return node switch
        {
            ClassBlockSyntax cb        => ExtractAttrNames(cb.ClassStatement.AttributeLists),
            MethodBlockSyntax mb       => ExtractAttrNames(mb.SubOrFunctionStatement.AttributeLists),
            PropertyBlockSyntax pb     => ExtractAttrNames(pb.PropertyStatement.AttributeLists),
            FieldDeclarationSyntax fd  => ExtractAttrNames(fd.AttributeLists),
            _                          => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Extracts attribute type names from an AttributeListSyntax collection.
    /// Uses a.Name.ToString() for the clean type name (not a.ToString() which includes arguments).
    /// </summary>
    private static string[] ExtractAttrNames(SyntaxList<AttributeListSyntax> lists) =>
        lists.SelectMany(al => al.Attributes)
             .Select(a => a.Name.ToString())
             .ToArray();
}
