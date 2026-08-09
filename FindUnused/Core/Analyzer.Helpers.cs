namespace FindUnused;

/// <summary>
/// Analyzer helpers: confidence calculation, test-file detection, and diagnostic mode.
/// </summary>
public static partial class Analyzer
{
    // Diagnostic mode
    public static bool DiagnosticMode { get; set; } = false;

    private static int CalculateConfidence(string symbolKind, string accessibility, bool isParameter, bool isInTestFile, bool hasInterfaceImpl)
    {
        int confidence = 80;

        switch (symbolKind.ToLower())
        {
            case "parameter":
                confidence = 50;
                break;
            case "field":
                confidence = 70;
                break;
            case "property":
                confidence = 75;
                break;
            case "method":
                confidence = 80;
                break;
            case "type":
                confidence = 90;
                break;
            default:
                confidence = 75;
                break;
        }

        if (accessibility == "Public")
            confidence -= 10;
        else if (accessibility == "Private")
            confidence += 10;

        if (isParameter)
            confidence = 50;

        if (isInTestFile)
            confidence -= 20;

        if (hasInterfaceImpl)
            confidence -= 15;

        return Math.Clamp(confidence, 0, 100);
    }

    private static SyntaxTree? GetSyntaxTree(Document? doc)
    {
        if (doc == null) return null;
        return doc.TryGetSyntaxTree(out var tree) ? tree : null;
    }

    private static bool IsInTestFile(SyntaxTree? tree)
    {
        if (tree == null) return false;
        var filePath = tree.FilePath;
        return filePath.Contains(".test", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains(".tests", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains("test/", StringComparison.OrdinalIgnoreCase) ||
               filePath.Contains("/test", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".test.cs", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".tests.cs", StringComparison.OrdinalIgnoreCase);
    }
}
