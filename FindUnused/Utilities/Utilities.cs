namespace FindUnused;

/// <summary>
/// Utility methods for path handling, display formatting, and framework detection
/// </summary>
public static class Utilities
{
    private static readonly string[] s_exclusionIndicators =
    [
        "/.nuget/packages/",
        "/.nuget/",
        "/packages/",
        "/bin/",
        "/obj/",
        "/debug/",
        "/release/"
    ];

    /// <summary>
    /// Determine if the given file path is located inside an excluded folder (NuGet/global packages, bin, obj, debug).
    /// Uses a fast ordinal-ignore-case check with a single normalization pass.
    /// </summary>
    public static bool IsPathExcluded(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        // Fast path: if the path doesn't contain any potential separator or indicator, skip
        // Most source paths are short and don't match exclusion patterns
        ReadOnlySpan<char> span = filePath.AsSpan();
        bool hasSeparator = false;
        foreach (char c in span)
        {
            if (c is '\\' or '/')
            {
                hasSeparator = true;
                break;
            }
        }
        if (!hasSeparator) return false;

        // Single normalization: replace backslashes with forward slashes
        // This is one allocation instead of the previous two (Replace + ToLowerInvariant)
        string normalized = filePath.Replace('\\', '/');

        // Check indicators using ordinal-ignore-case comparison (no ToLower allocation)
        foreach (string indicator in s_exclusionIndicators)
        {
            if (normalized.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
