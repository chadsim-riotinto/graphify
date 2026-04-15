using System.Text.RegularExpressions;

namespace VbNetSidecar.Extraction;

public static partial class NodeIdBuilder
{
    /// <summary>
    /// Build a stable node ID from name parts. Mirrors Python _make_id():
    /// join with _, strip non-alphanumeric, strip leading/trailing _, lowercase.
    /// </summary>
    public static string MakeId(params string[] parts)
    {
        var combined = string.Join("_",
            parts.Where(p => !string.IsNullOrEmpty(p))
                 .Select(p => p.Trim('_', '.')));
        var cleaned = NonAlphaNumericRegex().Replace(combined, "_");
        return cleaned.Trim('_').ToLowerInvariant();
    }

    /// <summary>
    /// Get the file stem ID for a .vb file path.
    /// "Source/CPR/DBClasses.vb" -> "dbclasses"
    /// "Source/CPR/frmOverview.Designer.vb" -> "frmoverview_designer"
    /// Strips the final .vb extension, then applies MakeId to the remaining stem.
    /// Per D-01: file-stem anchored IDs. Per D-02: no namespace in ID.
    /// </summary>
    public static string GetFileStemId(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        // Strip .vb extension (case-insensitive)
        if (fileName.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^3];
        return MakeId(fileName);
    }

    [GeneratedRegex(@"[^a-zA-Z0-9]+")]
    private static partial Regex NonAlphaNumericRegex();
}
