namespace FindUnused;

/// <summary>
/// Utilities for calculating confidence and severity of findings
/// </summary>
public static class FindingMetrics
{
    private static readonly StringComparison s_ordinalIgnoreCase = StringComparison.OrdinalIgnoreCase;

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
        if (accessibility.Equals("public", s_ordinalIgnoreCase))
        {
            confidence -= 30;
        }
        else if (accessibility.Equals("protected", s_ordinalIgnoreCase))
        {
            confidence -= 20;
        }
        else if (accessibility.Equals("internal", s_ordinalIgnoreCase))
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
            (accessibility.Equals("private", s_ordinalIgnoreCase) ||
             accessibility.Equals("internal", s_ordinalIgnoreCase)))
        {
            return "warning";
        }

        // Public symbols or lower confidence = information
        if (accessibility.Equals("public", s_ordinalIgnoreCase) || confidence < 60)
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
        ReadOnlySpan<char> kind = symbolKind.AsSpan();
        if (kind.Contains("class", s_ordinalIgnoreCase) || kind.Contains("type", s_ordinalIgnoreCase))
            return "🔷";
        if (kind.Contains("interface", s_ordinalIgnoreCase))
            return "🔶";
        if (kind.Contains("method", s_ordinalIgnoreCase) || kind.Contains("function", s_ordinalIgnoreCase))
            return "⚙️";
        if (kind.Contains("property", s_ordinalIgnoreCase))
            return "📝";
        if (kind.Contains("field", s_ordinalIgnoreCase))
            return "📦";
        if (kind.Contains("parameter", s_ordinalIgnoreCase))
            return "🎯";
        if (kind.Contains("enum", s_ordinalIgnoreCase))
            return "🔢";
        if (kind.Contains("struct", s_ordinalIgnoreCase))
            return "📐";
        if (kind.Contains("event", s_ordinalIgnoreCase))
            return "⚡";
        return "⚠️";
    }
}
