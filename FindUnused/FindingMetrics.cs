namespace FindUnused;

/// <summary>
/// Utilities for calculating confidence and severity of findings
/// </summary>
public static class FindingMetrics
{
    /// <summary>
    /// Calculate confidence level for a finding (0-100)
    /// Higher confidence = more certain this is truly unused
    /// </summary>
    public static int CalculateConfidence(
        ISymbol symbol,
        string accessibility,
        string symbolKind,
        bool hasReferences)
    {
        int confidence = 100;

        // Start with base confidence
        if (hasReferences)
        {
            // If we found references, confidence is very low
            return 0;
        }

        // Reduce confidence for public symbols (might be used externally)
        if (accessibility.Equals("public", StringComparison.OrdinalIgnoreCase))
        {
            confidence -= 30;
        }
        else if (accessibility.Equals("protected", StringComparison.OrdinalIgnoreCase))
        {
            confidence -= 20;
        }
        else if (accessibility.Equals("internal", StringComparison.OrdinalIgnoreCase))
        {
            confidence -= 10;
        }

        // Interfaces, abstract members might be implemented elsewhere
        if (symbol is IMethodSymbol method)
        {
            if (method.IsAbstract || method.IsVirtual)
            {
                confidence -= 20;
            }
            if (method.IsOverride)
            {
                confidence -= 10;
            }
        }

        // Properties with getters/setters might be used via reflection
        if (symbol is IPropertySymbol)
        {
            confidence -= 5;
        }

        // Events are often unused in interfaces
        if (symbol is IEventSymbol)
        {
            confidence -= 10;
        }

        // Partial types might have implementations elsewhere
        if (symbol is INamedTypeSymbol type)
        {
            // Check if it's a partial type by looking at declarations
            var declarations = type.DeclaringSyntaxReferences;
            if (declarations.Length > 1)
            {
                confidence -= 15;
            }
        }

        return Math.Max(0, Math.Min(100, confidence));
    }

    /// <summary>
    /// Calculate severity for a finding
    /// </summary>
    public static string CalculateSeverity(
        string accessibility,
        string symbolKind,
        int confidence)
    {
        // High confidence private/internal unused code = warning
        if (confidence >= 80 && 
            (accessibility.Equals("private", StringComparison.OrdinalIgnoreCase) ||
             accessibility.Equals("internal", StringComparison.OrdinalIgnoreCase)))
        {
            return "warning";
        }

        // Public symbols or lower confidence = information
        if (accessibility.Equals("public", StringComparison.OrdinalIgnoreCase) || confidence < 60)
        {
            return "information";
        }

        // Medium confidence private/internal = hint
        if (confidence >= 60)
        {
            return "hint";
        }

        return "information";
    }

    /// <summary>
    /// Get icon for finding based on symbol kind
    /// </summary>
    public static string GetIconForSymbolKind(string symbolKind)
    {
        return symbolKind.ToLowerInvariant() switch
        {
            var s when s.Contains("class") || s.Contains("type") => "🔷",
            var s when s.Contains("interface") => "🔶",
            var s when s.Contains("method") || s.Contains("function") => "⚙️",
            var s when s.Contains("property") => "📝",
            var s when s.Contains("field") => "📦",
            var s when s.Contains("parameter") => "🎯",
            var s when s.Contains("enum") => "🔢",
            var s when s.Contains("struct") => "📐",
            var s when s.Contains("event") => "⚡",
            _ => "⚠️"
        };
    }
}
