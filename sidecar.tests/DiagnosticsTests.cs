using VbNetSidecar.Extraction;
using Xunit;
using System.IO;

namespace VbNetSidecar.Tests;

/// <summary>
/// Tests for SIDE-12 and D-05/D-06: diagnostics are captured alongside partial extraction,
/// never blocking node/edge output.
/// </summary>
public class DiagnosticsTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // -----------------------------------------------------------------------
    // SIDE-12, D-06 — Broken file emits nodes AND diagnostics (non-blocking)
    // -----------------------------------------------------------------------

    [Fact]
    public void BrokenFileEmitsNodesAndDiagnostics()
    {
        var result = VbExtractor.Extract(FixturePath("broken_syntax.vb"));

        // Partial extraction must succeed — nodes exist despite parse errors
        Assert.NotEmpty(result.Nodes);

        // Parse errors must be captured
        Assert.NotEmpty(result.Diagnostics);

        // Valid symbols from the broken file are still extracted
        Assert.Contains(result.Nodes, n => n.Label == "BrokenClass");
        Assert.Contains(result.Nodes, n => n.Label == "ValidMethod");
        Assert.Contains(result.Nodes, n => n.Label == "AnotherValidMethod");
    }

    // -----------------------------------------------------------------------
    // D-05 — Diagnostics have correct structure
    // -----------------------------------------------------------------------

    [Fact]
    public void DiagnosticsHaveCorrectFormat()
    {
        var result = VbExtractor.Extract(FixturePath("broken_syntax.vb"));

        foreach (var diag in result.Diagnostics)
        {
            Assert.False(string.IsNullOrEmpty(diag.File),     "Diagnostic has empty File");
            Assert.False(string.IsNullOrEmpty(diag.Severity), "Diagnostic has empty Severity");
            Assert.False(string.IsNullOrEmpty(diag.Message),  "Diagnostic has empty Message");
            Assert.True(diag.Line > 0, $"Diagnostic Line must be > 0, got {diag.Line}");
        }
    }

    // -----------------------------------------------------------------------
    // Baseline: clean file produces zero diagnostics
    // -----------------------------------------------------------------------

    [Fact]
    public void CleanFileHasNoDiagnostics()
    {
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        Assert.Empty(result.Diagnostics);
    }
}
