namespace FindUnused;

/// <summary>
/// Utility methods for symbol analysis and Roslyn operations
/// </summary>
public static class SymbolUtilities
{
    private const int GeneratedCodeCheckLines = 10;
    /// <summary>
    /// Get the source location of a symbol
    /// </summary>
    public static Location? GetSourceLocation(ISymbol symbol)
        => symbol.Locations.FirstOrDefault(l => l.IsInSource);

    /// <summary>
    /// Get line and column position from a location
    /// </summary>
    public static (int line, int col) GetLinePosition(Location loc)
    {
        if (loc == null || loc.SourceTree == null) return (-1, -1);
        var pos = loc.GetLineSpan().StartLinePosition;
        return (pos.Line + 1, pos.Character + 1);
    }

    /// <summary>
    /// Check if a syntax tree contains generated code markers
    /// </summary>
    public static bool IsGenerated(SyntaxTree? tree, HashSet<string> generatedMarkers)
    {
        if (tree == null) return false;
        var text = tree.GetText();

        var lines = text.Lines.Take(GeneratedCodeCheckLines);
        foreach (var line in lines)
        {
            var lowerLine = line.ToString().ToLowerInvariant();
            if (generatedMarkers.Any(marker => lowerLine.Contains(marker)))
            {
                return true;
            }
        }

        // Check file-level attributes
        var root = tree.GetRoot();
        if (root != null)
        {
            var attributes = root.DescendantNodes().OfType<AttributeListSyntax>();
            foreach (var attrList in attributes)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var attrName = attr.Name.ToString().ToLowerInvariant();
                    if (generatedMarkers.Any(marker => attrName.Contains(marker.ToLowerInvariant())))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a namespace is allowed based on declared namespaces
    /// </summary>
    public static bool IsNamespaceAllowed(INamespaceSymbol nsSymbol, HashSet<string> declaredNamespaces)
    {
        if (declaredNamespaces == null || declaredNamespaces.Count == 0)
            return true;

        if (nsSymbol == null || nsSymbol.IsGlobalNamespace)
            return declaredNamespaces.Contains(string.Empty);

        var ns = nsSymbol.ToDisplayString();

        if (declaredNamespaces.Contains(ns))
            return true;

        var nsParts = ns.Split('.');
        for (int i = 1; i <= nsParts.Length; i++)
        {
            var parentNs = string.Join(".", nsParts.Take(i));
            if (declaredNamespaces.Contains(parentNs))
                return true;

            foreach (var declared in declaredNamespaces)
            {
                if (declared.StartsWith(parentNs + ".", StringComparison.Ordinal))
                    return true;
            }
        }

        foreach (var declared in declaredNamespaces)
        {
            if (AreNamespacesRelated(ns, declared))
                return true;
        }

        return false;
    }

    private static bool AreNamespacesRelated(string ns1, string ns2)
    {
        return ns1.StartsWith(ns2 + ".", StringComparison.Ordinal) ||
               ns2.StartsWith(ns1 + ".", StringComparison.Ordinal);
    }

    /// <summary>
    /// Build project display name from project and document
    /// </summary>
    public static string BuildProjectDisplayNameFrom(Project? fallbackProject, Document? doc)
    {
        var name = doc?.Project?.Name ?? fallbackProject?.Name ?? "Unknown";
        var path = doc?.Project?.FilePath ?? fallbackProject?.FilePath ?? "(unknown)";
        return $"{name} ({path})";
    }

    /// <summary>
    /// Get icon for symbol kind
    /// </summary>
    public static string GetIconForSymbolKind(string symbolKind)
        => symbolKind switch
        {
            "Type" => "🔷",
            "Method" => "🔶",
            "Property" => "🔸",
            "Field" => "🔹",
            "Parameter" => "🎯",
            _ => "❓"
        };

    /// <summary>
    /// Check if a method is in an API controller class
    /// </summary>
    public static bool IsApiControllerMethod(INamedTypeSymbol containingType)
    {
        if (containingType == null) return false;

        if (containingType.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var baseType = containingType.BaseType;
        while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
        {
            var baseTypeName = baseType.Name.ToLowerInvariant();
            if (baseTypeName == "controller" ||
                baseTypeName == "controllerbase" ||
                baseTypeName == "apicontroller")
            {
                return true;
            }
            baseType = baseType.BaseType;
        }

        var apiControllerAttribute = containingType.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name.Equals("ApiControllerAttribute", StringComparison.OrdinalIgnoreCase) == true);

        if (apiControllerAttribute != null) return true;

        return false;
    }

    /// <summary>
    /// Check if a method is a test method
    /// </summary>
    public static bool IsTestMethod(IMethodSymbol method, INamedTypeSymbol containingType, HashSet<string> testAttributes, HashSet<string> testClassAttributes)
    {
        if (method == null) return false;

        foreach (var attr in method.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName != null && testAttributes.Any(testAttr =>
                attrName.Equals(testAttr, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (containingType != null)
        {
            foreach (var attr in containingType.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (attrName != null && testClassAttributes.Any(testAttr =>
                    attrName.Equals(testAttr, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            var className = containingType.Name;
            if (className.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                className.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a method is an entry point
    /// </summary>
    public static bool IsEntryPointMethod(IMethodSymbol method, INamedTypeSymbol containingType, HashSet<string> testAttributes, HashSet<string> testClassAttributes)
    {
        if (method == null) return false;

        if (IsApiControllerMethod(containingType)) return true;
        if (IsTestMethod(method, containingType, testAttributes, testClassAttributes)) return true;

        // Add modern framework support
        if (IsGrpcServiceMethod(method, containingType)) return true;
        if (IsSignalRHubMethod(method, containingType)) return true;
        if (IsBlazorComponentMethod(method, containingType)) return true;
        if (IsBackgroundServiceMethod(method, containingType)) return true;
        if (IsEventHandlerMethod(method)) return true;
        if (IsDependencyInjectionMethod(method, containingType)) return true;

        return false;
    }

    private static bool IsGrpcServiceMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;
        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        return baseTypeName.Contains("servicebase") &&
               method.MethodKind == MethodKind.Ordinary;
    }

    private static bool IsSignalRHubMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;

        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        if (baseTypeName == "hub" && method.DeclaredAccessibility == Accessibility.Public)
        {
            return true;
        }

        return containingType.AllInterfaces.Any(iface =>
            iface.Name.Equals("Hub", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlazorComponentMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;

        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        if (baseTypeName == "componentbase")
        {
            if (method.Name.StartsWith("On", StringComparison.Ordinal) ||
                method.Name.StartsWith("Handle", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBackgroundServiceMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;

        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        if (baseTypeName == "backgroundservice")
        {
            return method.Name == "ExecuteAsync" ||
                   method.Name.StartsWith("On", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsEventHandlerMethod(IMethodSymbol method)
    {
        var parameters = method.Parameters;
        if (parameters.Length < 2) return false;

        var firstParam = parameters[0].Type;
        var secondParam = parameters[1].Type;

        if (firstParam.Name == "Object" &&
            (secondParam.Name.EndsWith("EventArgs", StringComparison.Ordinal) ||
             secondParam.Name == "EventArgs"))
        {
            return true;
        }

        return method.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name.EndsWith("EventHandlerAttribute", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool IsDependencyInjectionMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType == null) return false;

        var methodName = method.Name.ToLowerInvariant();
        var className = containingType.Name.ToLowerInvariant();

        if ((className == "startup" || className == "program") &&
            (methodName.Contains("configure") || methodName.Contains("add") || methodName.Contains("service")))
        {
            return true;
        }

        return false;
    }
}