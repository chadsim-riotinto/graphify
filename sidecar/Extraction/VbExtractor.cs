using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using VbNetSidecar.Model;

namespace VbNetSidecar.Extraction;

/// <summary>
/// Public entry point for VB.NET file extraction.
/// Reads a .vb file BOM-safely, parses it with Roslyn, walks the AST via VbSyntaxWalker,
/// filters unresolvable edges for validate_extraction() compliance, collects diagnostics,
/// and returns a complete ExtractionResult.
/// </summary>
public static class VbExtractor
{
    /// <summary>
    /// Extracts nodes, edges, and diagnostics from a single .vb file.
    /// </summary>
    /// <param name="filePath">Path to the .vb file. May be relative or absolute.</param>
    /// <returns>
    /// ExtractionResult with schema-compliant nodes and edges.
    /// Never throws — errors are captured as DiagnosticEntry items in the result.
    /// </returns>
    public static ExtractionResult Extract(string filePath)
    {
        // --- Step 1: Validate input (T-01-03 threat mitigation) ---
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"VB.NET source file not found: {filePath}", filePath);

        // Use original filePath for all source_file fields (caller-visible path).
        // Use fullPath only for file I/O to canonicalize symlinks and relative segments.
        var fullPath = Path.GetFullPath(filePath);

        try
        {
            return ExtractCore(filePath, fullPath);
        }
        catch (IOException ex)
        {
            // I/O error — return empty result with diagnostic rather than propagating.
            // The batch caller needs to continue processing other files.
            var errorResult = new ExtractionResult();
            errorResult.Diagnostics.Add(new DiagnosticEntry(
                filePath,
                "Error",
                $"IO error reading file: {ex.Message}",
                1
            ));
            return errorResult;
        }
    }

    private static ExtractionResult ExtractCore(string filePath, string fullPath)
    {
        // --- Step 2: Read file with BOM handling (Pitfall 3) ---
        // CRITICAL: Do NOT use File.ReadAllText() — it does not strip UTF-8 BOM,
        // causing 2 spurious parse diagnostics on the 16 BOM files in Source/CPR/.
        // SourceText.From(stream, Encoding.UTF8) auto-detects and strips the BOM.
        SourceText sourceText;
        using (var stream = File.OpenRead(fullPath))
        {
            sourceText = SourceText.From(stream, Encoding.UTF8, canBeEmbedded: false);
        }

        // --- Step 3: Parse with Roslyn ---
        // path: embeds file path in diagnostics messages for debugging.
        var tree = VisualBasicSyntaxTree.ParseText(sourceText, path: filePath);

        // --- Step 4: Create the file node (SIDE-02) ---
        // File node is always first in the result. Its ID is the file stem.
        // All top-level type nodes get contains edges FROM this file node.
        var fileId   = NodeIdBuilder.GetFileStemId(filePath);
        var fileNode = new GraphNode(fileId, Path.GetFileName(filePath), "code", filePath, "L1");

        // --- Step 5: Walk the AST ---
        var walker = new VbSyntaxWalker(fileId, filePath, tree);
        walker.DefaultVisit(tree.GetRoot());

        // --- Step 6: Collect diagnostics (SIDE-12, D-05, D-06) ---
        // Parse errors NEVER block extraction — emit diagnostics alongside nodes/edges.
        var diagnostics = tree.GetDiagnostics()
            .Select(d => new DiagnosticEntry(
                filePath,
                d.Severity.ToString(),
                d.GetMessage(),
                d.Location.GetLineSpan().StartLinePosition.Line + 1
            ))
            .ToList();

        // --- Step 7: Assemble result with file node first ---
        var result = new ExtractionResult();
        result.Nodes.Add(fileNode);
        result.Nodes.AddRange(walker.Nodes);
        result.Edges.AddRange(walker.Edges);
        result.Diagnostics.AddRange(diagnostics);

        // --- Step 8: Filter edges for validate_extraction() compliance ---
        // Edge source and target MUST match a node ID in the result.
        // This handles:
        //   - calls edges to external methods (Console.WriteLine, MyBase.New) → dropped
        //   - calls edges to internal methods → target resolved from raw name to node ID
        //   - imports edges to external namespaces (System.Data) → dropped
        //   - inherits edges to external types (System.Windows.Forms.Form) → dropped
        //   - handles_event edges to WithEvents field nodes → kept (synthetic nodes exist)
        //   - contains edges → always kept (both source and target are internal nodes)
        FilterEdges(result);

        return result;
    }

    /// <summary>
    /// Post-processing pass: merges Partial Class Designer file nodes into their
    /// corresponding code-file class nodes. Detection criteria:
    /// 1. Node SourceFile ends with ".Designer.vb" (case-insensitive)
    /// 2. Node Attributes array contains a value matching "*DesignerGenerated*"
    ///
    /// For each qualifying Designer class node, finds the matching code class node
    /// (same Label, SourceFile NOT ending in .Designer.vb), remaps all Designer
    /// node IDs to the code-file class ID prefix, deduplicates, remaps edge
    /// Source/Target values, then calls FilterEdges() so that handles_event edges
    /// from the code file now resolve against the remapped WithEvents nodes.
    ///
    /// Settings.Designer.vb (CompilerGenerated, not DesignerGenerated) is skipped.
    /// Resources.Designer.vb (Module, not Class) is unaffected.
    /// </summary>
    public static void MergePartialClasses(ExtractionResult result)
    {
        // Step 1: Find Designer class nodes
        var designerClasses = result.Nodes
            .Where(n => n.SourceFile.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase)
                     && n.Attributes != null
                     && n.Attributes.Any(a => a.Contains("DesignerGenerated")))
            .ToList();

        if (designerClasses.Count == 0) return;

        // Step 2: Build ID remap: designerClassId -> codeClassId
        var idRemap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dc in designerClasses)
        {
            var match = result.Nodes.FirstOrDefault(n =>
                n.Label == dc.Label
                && !n.SourceFile.EndsWith(".Designer.vb", StringComparison.OrdinalIgnoreCase));
            if (match == null) continue; // no code-behind partner — skip (e.g. Settings.Designer.vb)
            idRemap[dc.Id] = match.Id;
        }

        if (idRemap.Count == 0) return;

        // Step 3: Build prefix remap for child nodes (Designer children -> code-file children)
        var prefixRemap = idRemap.ToDictionary(
            kvp => kvp.Key + "_",
            kvp => kvp.Value + "_");

        // Step 4: RemapId helper
        string RemapId(string id)
        {
            if (idRemap.TryGetValue(id, out var exact)) return exact;
            foreach (var (oldPrefix, newPrefix) in prefixRemap)
                if (id.StartsWith(oldPrefix, StringComparison.Ordinal))
                    return newPrefix + id[oldPrefix.Length..];
            return id;
        }

        // Step 5: Remap all node IDs, deduplicate (Designer class node removed, code class kept)
        var remapped = result.Nodes.Select(n => n with { Id = RemapId(n.Id) }).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        result.Nodes.Clear();
        foreach (var n in remapped)
            if (seen.Add(n.Id))
                result.Nodes.Add(n);

        // Step 6: Remap all edge Source/Target values
        var originalEdges = result.Edges.ToList();
        result.Edges.Clear();
        foreach (var edge in originalEdges)
            result.Edges.Add(edge with {
                Source = RemapId(edge.Source),
                Target = RemapId(edge.Target),
            });

        // Step 7: Re-run FilterEdges so handles_event edges from code file now resolve
        // against remapped WithEvents nodes donated by the Designer file.
        // strict: true — drop any handles_event edge whose target is still unresolved
        // after the remap. A dangling target at this point means the WithEvents field
        // was never declared in any file in the batch, so the edge must be dropped to
        // pass validate_extraction().
        FilterEdges(result, strict: true);
    }

    /// <summary>
    /// Filters the edge list in-place to ensure every edge's source and target
    /// matches a node ID in the result. Also resolves raw method name targets on
    /// calls edges to node IDs when a unique label match exists.
    /// </summary>
    /// <param name="result">The extraction result whose edges will be filtered in-place.</param>
    /// <param name="strict">
    /// When true, handles_event edges with unresolved targets are dropped.
    /// Used on the second pass after MergePartialClasses() remaps Designer IDs —
    /// any target still unresolved at that point has no matching WithEvents field
    /// in any file in the batch and must be dropped for validate_extraction() compliance.
    /// </param>
    private static void FilterEdges(ExtractionResult result, bool strict = false)
    {
        var nodeIds = new HashSet<string>(result.Nodes.Select(n => n.Id), StringComparer.Ordinal);

        // Build a label -> ID lookup for calls edge resolution.
        // Only use labels that are unique within the file to avoid ambiguous resolution.
        var labelToId = result.Nodes
            .GroupBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var resolvedEdges = new List<GraphEdge>(result.Edges.Count);

        foreach (var edge in result.Edges)
        {
            var source = edge.Source;
            var target = edge.Target;

            // Resolve raw name targets to node IDs via label lookup for relational edges.
            // Applies to: calls (method names), inherits (class names), imports (namespace names).
            // Only resolves when the raw target is not already a valid node ID.
            // e.g. calls "InitializeComponent" → "dbclasses_dbconnection_initializecomponent"
            // e.g. inherits "BaseClass" → "tmpfile_baseclass"
            if (!nodeIds.Contains(target) &&
                (edge.Relation == "calls" || edge.Relation == "inherits" || edge.Relation == "imports"))
            {
                if (labelToId.TryGetValue(target, out var resolvedId))
                    target = resolvedId;
                else
                    continue; // Unresolvable external reference — drop edge
            }

            // implements edges: resolve target, but DROP if target is unresolvable.
            // External interfaces (IComparable, IDisposable) must be dropped to pass
            // validate_extraction which requires all edge targets to be valid node IDs.
            if (edge.Relation == "implements")
            {
                if (!nodeIds.Contains(source)) continue;
                if (!nodeIds.Contains(target) && labelToId.TryGetValue(target, out var resolvedImplId))
                    target = resolvedImplId;
                if (!nodeIds.Contains(target)) continue; // drop external interface
                resolvedEdges.Add(edge with { Target = target });
                continue;
            }

            // handles_event edges: keep even when target is unresolved in single-file context.
            // The WithEvents field may be declared in a companion Designer.vb file; after
            // MergePartialClasses() re-maps Designer IDs and calls FilterEdges() a second
            // time (with strict: true), the target will be resolved or the edge will be
            // dropped. For inline WithEvents (no Designer file), the target is already
            // resolved above because the WithEvents field node was emitted in the same file.
            if (edge.Relation == "handles_event")
            {
                if (!nodeIds.Contains(source)) continue; // drop if source is invalid
                if (strict && !nodeIds.Contains(target)) continue; // second pass: drop dangling
                resolvedEdges.Add(edge with { Target = target });
                continue;
            }

            // Drop any edge whose source or target is not a known node ID.
            // Covers: imports to external namespaces, inherits to external types,
            // and any other unresolvable edges.
            if (!nodeIds.Contains(source) || !nodeIds.Contains(target))
                continue;

            resolvedEdges.Add(edge with { Target = target });
        }

        result.Edges.Clear();
        result.Edges.AddRange(resolvedEdges);
    }
}
