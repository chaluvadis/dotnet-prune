namespace FindUnused;

/// <summary>
/// Property analysis: AnalyzePropertyAsync.
/// </summary>
public static partial class Analyzer
{
    /// <summary>
    /// Analyze property usage
    /// </summary>
    private static async Task<(List<Finding> findings, bool referenced)> AnalyzePropertyAsync(
        IPropertySymbol prop,
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        bool includePublic,
        bool includeInternal,
        bool excludeGenerated,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        AnalyzerCache cache,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool referenced = false;
        if (prop.IsImplicitlyDeclared) return (findings, referenced);
        var acc = prop.DeclaredAccessibility;
        if (acc == Accessibility.Public && !includePublic) return (findings, referenced);
        if (acc == Accessibility.Internal && !includeInternal) return (findings, referenced);
        if (acc == Accessibility.Protected || acc == Accessibility.ProtectedOrInternal) return (findings, referenced);
        if (prop.IsOverride || prop.ExplicitInterfaceImplementations.Any()) return (findings, referenced);
        var defLocProp = SymbolUtilities.GetSourceLocation(prop);

        // Skip if property declaration is in an excluded folder
        if (defLocProp != null && PathUtilities.SourceTreeIsExcluded(defLocProp.SourceTree, cache.ExclusionPatterns)) return (findings, referenced);

        if (excludeGenerated && defLocProp != null && SymbolUtilities.IsGenerated(defLocProp.SourceTree, cache.GeneratedCodeMarkers)) return (findings, referenced);

        var (refCount, _) = await CheckSymbolReferencesAsync(prop, project, solution, solutionProjectIds, isReferenceInSolutionSource, cache);

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocProp != null ? SymbolUtilities.GetLinePosition(defLocProp) : (-1, -1);
            Document? doc = defLocProp != null ? solution.GetDocument(defLocProp.SourceTree) : null;
            string projectDisplay = SymbolUtilities.BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = PathUtilities.GetDisplayPathForDocument(doc, defLocProp?.SourceTree, project, solution);
            string displayName = PathUtilities.GetDisplayNameForDocument(doc, defLocProp?.SourceTree);
            string fullPath = PathUtilities.GetFullPathForDocument(doc, defLocProp?.SourceTree) ?? "(generated)";
            string projectFilePath = PathUtilities.GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = SymbolUtilities.GetIconForSymbolKind(SymbolKindProperty);

            findings.Add(new Finding
            {
                Project = projectDisplay,
                FilePath = fullPath,
                FilePathDisplay = filePathDisplay,
                DisplayName = displayName,
                ProjectFilePath = projectFilePath,
                Line = line,
                SymbolKind = "Property",
                ContainingType = type.ToDisplayString(),
                SymbolName = prop.ToDisplayString(),
                Accessibility = prop.DeclaredAccessibility.ToString(),
                Remarks = "No references found in solution source",
                DeclaredProject = declaredProject,
                FallbackProject = fallbackProject,
                Icon = icon,
                Confidence = CalculateConfidence("property", prop.DeclaredAccessibility.ToString(), false, IsInTestFile(GetSyntaxTree(doc)), prop.ExplicitInterfaceImplementations.Any())
            });
            EnrichFinding(findings[^1], prop, false);
            progress?.Report($"    Unused property: {type.ToDisplayString()}.{prop.Name} [{prop.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }
        return (findings, referenced);
    }
}
