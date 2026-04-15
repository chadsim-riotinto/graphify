using VbNetSidecar.Extraction;
using Xunit;
using System.IO;

namespace VbNetSidecar.Tests;

/// <summary>
/// Tests for TEST-04: encoding edge cases.
/// Validates UTF-8 BOM files parse cleanly (zero spurious diagnostics)
/// and ASCII CRLF files parse cleanly.
/// </summary>
public class EncodingTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // -----------------------------------------------------------------------
    // TEST-04 — UTF-8 BOM file parses with zero diagnostics
    // -----------------------------------------------------------------------

    [Fact]
    public void BomFileParsesClearlyWithZeroDiagnostics()
    {
        // bom_file.vb is UTF-8 with BOM — SourceText.From(stream, Encoding.UTF8) strips it
        var result = VbExtractor.Extract(FixturePath("bom_file.vb"));

        // BOM must not cause spurious parse errors
        Assert.Empty(result.Diagnostics);

        // Symbols extracted correctly despite BOM prefix
        Assert.Contains(result.Nodes, n => n.Label == "BomClass");
        Assert.Contains(result.Nodes, n => n.Label == "BomMethod");
    }

    // -----------------------------------------------------------------------
    // Baseline: ASCII CRLF file parses cleanly
    // -----------------------------------------------------------------------

    [Fact]
    public void AsciiFileParsesClearly()
    {
        // simple_class.vb is ASCII CRLF — standard legacy encoding
        var result = VbExtractor.Extract(FixturePath("simple_class.vb"));

        Assert.Empty(result.Diagnostics);
    }
}
