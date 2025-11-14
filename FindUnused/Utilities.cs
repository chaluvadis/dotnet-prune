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


    /// <summary>
    /// Check if a syntax tree is excluded based on its file path
    /// </summary>
    public static bool SourceTreeIsExcluded(SyntaxTree? tree)
    {
        if (tree == null) return false;
        return IsPathExcluded(tree.FilePath);
    }

    /// <summary>
    /// Get a file path for display in findings. Prefer the project's virtual folder structure (Document.Folders),
    /// otherwise make the path relative to the project folder (Directory of the .csproj), then relative to the solution,
    /// and finally fall back to the file name or the full path if needed.
    /// This attempts to replicate how Visual Studio shows files under a project in Solution Explorer.
    /// Returns the display path (project-relative / virtual folder path).
    /// </summary>
    public static string GetDisplayPathForDocument(Document? doc, SyntaxTree? tree, Project? project, Solution? solution)
    {
        // If we have a Roslyn Document and it has virtual Folders, prefer that (this is how VS organizes files logically)
        try
        {
            if (doc != null)
            {
                if (doc.Folders != null && doc.Folders.Count > 0)
                {
                    var folderPart = Path.Combine([.. doc.Folders]);
                    var fileName = !string.IsNullOrEmpty(doc.FilePath) ? Path.GetFileName(doc.FilePath) : "(in-memory)";
                    return Path.Combine(folderPart, fileName);
                }

                // If the file physically sits inside the project directory, show relative path to the project file
                if (!string.IsNullOrEmpty(doc.FilePath) && project?.FilePath != null)
                {
                    var projectDir = Path.GetDirectoryName(project.FilePath) ?? "";
                    try
                    {
                        if (!string.IsNullOrEmpty(projectDir) && Path.GetFullPath(doc.FilePath).StartsWith(Path.GetFullPath(projectDir), StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = Path.GetRelativePath(projectDir, doc.FilePath);
                            return rel.Replace('/', Path.DirectorySeparatorChar);
                        }
                    }
                    catch
                    {
                        // fall back if any path operations fail
                    }
                }

                // If file is not inside project, try relative to solution directory
                if (!string.IsNullOrEmpty(doc.FilePath) && solution?.FilePath != null)
                {
                    var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? "";
                    try
                    {
                        if (!string.IsNullOrEmpty(solutionDir) && Path.GetFullPath(doc.FilePath).StartsWith(Path.GetFullPath(solutionDir), StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = Path.GetRelativePath(solutionDir, doc.FilePath);
                            return rel.Replace('/', Path.DirectorySeparatorChar);
                        }
                    }
                    catch
                    {
                        // ignore and fall back
                    }
                }

                // Otherwise, if doc has a FilePath use only the filename (common for linked files)
                if (!string.IsNullOrEmpty(doc.FilePath))
                    return Path.GetFileName(doc.FilePath);
            }

            // Fallback: if a syntax tree exists (e.g., when symbol location provided) use similar logic
            if (tree != null && !string.IsNullOrEmpty(tree.FilePath))
            {
                var filePath = tree.FilePath;
                if (project?.FilePath != null)
                {
                    var projectDir = Path.GetDirectoryName(project.FilePath) ?? "";
                    try
                    {
                        if (!string.IsNullOrEmpty(projectDir) && Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(projectDir), StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = Path.GetRelativePath(projectDir, filePath);
                            return rel.Replace('/', Path.DirectorySeparatorChar);
                        }
                    }
                    catch { }
                }
                if (solution?.FilePath != null)
                {
                    var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? "";
                    try
                    {
                        if (!string.IsNullOrEmpty(solutionDir) && Path.GetFullPath(filePath).StartsWith(Path.GetFullPath(solutionDir), StringComparison.OrdinalIgnoreCase))
                        {
                            var rel = Path.GetRelativePath(solutionDir, filePath);
                            return rel.Replace('/', Path.DirectorySeparatorChar);
                        }
                    }
                    catch { }
                }

                return Path.GetFileName(filePath);
            }
        }
        catch
        {
            // keep fallback behavior below
        }

        return "(generated)";
    }

    /// <summary>
    /// Compute a short DisplayName (filename) from a document/tree for easy UI display.
    /// Falls back to the last path segment or "(generated)".
    /// </summary>
    public static string GetDisplayNameForDocument(Document? doc, SyntaxTree? tree)
    {
        try
        {
            if (doc != null && !string.IsNullOrEmpty(doc.FilePath))
                return Path.GetFileName(doc.FilePath);
            if (tree != null && !string.IsNullOrEmpty(tree.FilePath))
                return Path.GetFileName(tree.FilePath);
        }
        catch { /* ignore path errors */ }

        return "(generated)";
    }

    /// <summary>
    /// Get an absolute file path (full path) for the given document/tree if available.
    /// Falls back to null if not available.
    /// </summary>
    public static string? GetFullPathForDocument(Document? doc, SyntaxTree? tree)
    {
        if (doc != null && !string.IsNullOrEmpty(doc.FilePath))
            return Path.GetFullPath(doc.FilePath);
        if (tree != null && !string.IsNullOrEmpty(tree.FilePath))
            return Path.GetFullPath(tree.FilePath);
        return null;
    }

    /// <summary>
    /// Get absolute project file path if available.
    /// </summary>
    public static string? GetProjectFilePath(Project? project, Document? doc)
    {
        // prefer project.FilePath, else try doc.Project.FilePath
        if (project?.FilePath != null && !string.IsNullOrEmpty(project.FilePath)) return Path.GetFullPath(project.FilePath);
        if (doc?.Project?.FilePath != null && !string.IsNullOrEmpty(doc.Project.FilePath)) return Path.GetFullPath(doc.Project.FilePath);
        return null;
    }

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
    public static bool IsGenerated(SyntaxTree? tree)
    {
        if (tree == null) return false;
        var text = tree.GetText();

        // Enhanced patterns for modern .NET
        var markers = new[]
        {
            "<auto-generated", "generated by", "<autogenerated",
            "// <auto-generated", "// This code generated by",
            "[GeneratedCode]", "@generated", "partial class",
            "[JsonSerializable]", "[CompilerGenerated]"
        };

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
                    if (attrName.Contains("generatedcode") ||
                        attrName.Contains("jsonserializable") ||
                        attrName.Contains("compilergenerated"))
                    {
                        return true;
                    }
                }
            }
        }

        // Check file header comments and directives
        var lines = text.Lines.Take(10);
        foreach (var line in lines)
        {
            var lowerLine = line.ToString().ToLowerInvariant();
            if (markers.Any(marker => lowerLine.Contains(marker)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a namespace is allowed based on declared namespaces
    /// </summary>
    public static bool IsNamespaceAllowed(INamespaceSymbol nsSymbol, HashSet<string> declaredNamespaces)
    {
        // If declaredNamespaces was left intentionally empty (no filtering), allow everything
        if (declaredNamespaces == null || declaredNamespaces.Count == 0)
            return true;

        // If the type is in the global namespace, allow only if solution declared global types
        if (nsSymbol == null || nsSymbol.IsGlobalNamespace)
            return declaredNamespaces.Contains(string.Empty);

        var ns = nsSymbol.ToDisplayString();

        // Exact match
        if (declaredNamespaces.Contains(ns))
            return true;

        // Parent namespace match (looser matching)
        var nsParts = ns.Split('.');
        for (int i = 1; i <= nsParts.Length; i++)
        {
            var parentNs = string.Join(".", nsParts.Take(i));
            if (declaredNamespaces.Contains(parentNs))
                return true;

            // Also check for partial matches
            foreach (var declared in declaredNamespaces)
            {
                if (declared.StartsWith(parentNs + ".", StringComparison.Ordinal))
                    return true;
            }
        }

        // Fuzzy matching for similar namespaces
        foreach (var declared in declaredNamespaces)
        {
            if (AreNamespacesRelated(ns, declared))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Check if two namespaces are related
    /// </summary>
    private static bool AreNamespacesRelated(string ns1, string ns2)
    {
        // Simple check: if one is prefix of the other
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
    {
        // Use simple emoji icons for quick visual identification. Adjust as you prefer.
        return symbolKind switch
        {
            "Type" => "📦",
            "Method" => "🔧",
            "Property" => "🔑",
            "Field" => "🧩",
            "Parameter" => "🎯",
            _ => "❓"
        };
    }

    /// <summary>
    /// Check if a method is in an API controller class
    /// </summary>
    public static bool IsApiControllerMethod(INamedTypeSymbol containingType)
    {
        if (containingType == null) return false;

        // Check if the class name ends with "Controller" (common MVC/Web API pattern)
        if (containingType.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check if the class inherits from Controller, ControllerBase, or has [ApiController] attribute
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

        // Check for [ApiController] attribute
        var apiControllerAttribute = containingType.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name.Equals("ApiControllerAttribute", StringComparison.OrdinalIgnoreCase) == true);

        if (apiControllerAttribute != null) return true;

        // Check for controller-related HTTP attributes on methods (indicates it's likely a controller)
        return false;
    }

    /// <summary>
    /// Check if a method is a test method
    /// </summary>
    public static bool IsTestMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (method == null) return false;

        // Check for common test framework attributes
        var testAttributes = new[]
        {
        "TestAttribute", "FactAttribute", "TheoryAttribute", "TestMethodAttribute",
        "TestCaseAttribute", "TestCaseSourceAttribute", "InlineDataAttribute",
        "ClassDataAttribute", "DynamicDataAttribute"
    };

        foreach (var attr in method.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;
            if (attrName != null && testAttributes.Any(testAttr =>
                attrName.Equals(testAttr, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Check if containing class has test-related attributes
        if (containingType != null)
        {
            var testClassAttributes = new[]
            {
            "TestClassAttribute", "TestFixtureAttribute", "TestCategoryAttribute"
        };

            foreach (var attr in containingType.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (attrName != null && testClassAttributes.Any(testAttr =>
                    attrName.Equals(testAttr, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            // Check if class name ends with "Test" or "Tests"
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
    /// Check if a method is an entry point (like a controller action)
    /// </summary>
    public static bool IsEntryPointMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (method == null) return false;

        // Existing support
        if (IsApiControllerMethod(containingType)) return true;
        if (IsTestMethod(method, containingType)) return true;

        // Add modern framework support
        if (IsGrpcServiceMethod(method, containingType)) return true;
        if (IsSignalRHubMethod(method, containingType)) return true;
        if (IsBlazorComponentMethod(method, containingType)) return true;
        if (IsBackgroundServiceMethod(method, containingType)) return true;

        // Add event handler detection
        if (IsEventHandlerMethod(method)) return true;

        // Add DI configuration detection
        if (IsDependencyInjectionMethod(method, containingType)) return true;

        return false;
    }

    /// <summary>
    /// Check if a method is a gRPC service method
    /// </summary>
    private static bool IsGrpcServiceMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;
        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        return baseTypeName.Contains("servicebase") &&
               method.MethodKind == MethodKind.Ordinary;
    }

    /// <summary>
    /// Check if a method is a SignalR hub method
    /// </summary>
    private static bool IsSignalRHubMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;

        // Check if the class inherits from SignalR Hub
        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        if (baseTypeName == "hub" && method.DeclaredAccessibility == Accessibility.Public)
        {
            return true;
        }

        // Check for SignalR hub interfaces
        return containingType.AllInterfaces.Any(iface =>
            iface.Name.Equals("Hub", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Check if a method is a Blazor component method
    /// </summary>
    private static bool IsBlazorComponentMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;

        // Check if the class inherits from Blazor ComponentBase
        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        if (baseTypeName == "componentbase")
        {
            // Blazor lifecycle methods
            if (method.Name.StartsWith("On", StringComparison.Ordinal) ||
                method.Name.StartsWith("Handle", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check if a method is a background service method
    /// </summary>
    private static bool IsBackgroundServiceMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType?.BaseType == null) return false;

        // Check if the class inherits from BackgroundService
        var baseTypeName = containingType.BaseType.Name.ToLowerInvariant();
        if (baseTypeName == "backgroundservice")
        {
            return method.Name == "ExecuteAsync" ||
                   method.Name.StartsWith("On", StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Check if a method is an event handler method
    /// </summary>
    private static bool IsEventHandlerMethod(IMethodSymbol method)
    {
        var parameters = method.Parameters;
        if (parameters.Length < 2) return false;

        var firstParam = parameters[0].Type;
        var secondParam = parameters[1].Type;

        // Common event handler pattern: void MethodName(object sender, EventArgs e)
        if (firstParam.Name == "Object" &&
            (secondParam.Name.EndsWith("EventArgs", StringComparison.Ordinal) ||
             secondParam.Name == "EventArgs"))
        {
            return true;
        }

        // Check for event handler attributes
        return method.GetAttributes().Any(attr =>
            attr.AttributeClass?.Name.EndsWith("EventHandlerAttribute", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// Check if a method is used in dependency injection configuration
    /// </summary>
    private static bool IsDependencyInjectionMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        if (containingType == null) return false;

        var methodName = method.Name.ToLowerInvariant();
        var className = containingType.Name.ToLowerInvariant();

        // Startup/Program class methods for DI configuration
        if ((className == "startup" || className == "program") &&
            (methodName.Contains("configure") || methodName.Contains("add") || methodName.Contains("service")))
        {
            return true;
        }

        return false;
    }
}