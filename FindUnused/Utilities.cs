namespace FindUnused;

/// <summary>
/// Utility methods for path handling, display formatting, and framework detection
/// </summary>
public static class Utilities
{
    /// <summary>
    /// Determine if the given file path is located inside an excluded folder (NuGet/global packages, bin, obj, debug).
    /// This is a lightweight textual check intentionally tolerant for cross-platform separators.
    /// </summary>
    public static bool IsPathExcluded(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var low = filePath.Replace('\\', '/').ToLowerInvariant();

        // indicators of paths we want to ignore
        var indicators = new[]
        {
            "/.nuget/packages/", // global NuGet cache
            "/.nuget/",          // any .nuget folder
            "/packages/",        // legacy packages folder
            "/bin/",             // build output
            "/obj/",             // intermediate output
            "/debug/",           // debug output folder (e.g. bin/Debug)
            "/release/"          // optionally ignore release outputs too (safe to include)
        };

        return indicators.Any(low.Contains);
    }















}