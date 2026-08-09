namespace FindUnused;

/// <summary>
/// Field analysis: AnalyzeFieldAsync.
/// </summary>
public static partial class Analyzer
{
    /// <summary>
    /// Analyze field usage
    /// </summary>
    private static async Task<(List<Finding> findings, bool referenced)> AnalyzeFieldAsync(
        IFieldSymbol field,
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
        if (field.IsImplicitlyDeclared) return (findings, referenced);
        var acc = field.DeclaredAccessibility;
        if (acc == Accessibility.Public && !includePublic) return (findings, referenced);
        if (acc == Accessibility.Internal && !includeInternal) return (findings, referenced);
        if (acc == Accessibility.Protected || acc == Accessibility.ProtectedOrInternal) return (findings, referenced);
        var defLocField = SymbolUtilities.GetSourceLocation(field);

        // Skip if field declaration is in an excluded folder
        if (defLocField != null && PathUtilities.SourceTreeIsExcluded(defLocField.SourceTree, cache.ExclusionPatterns)) return (findings, referenced);

        if (excludeGenerated && defLocField != null && SymbolUtilities.IsGenerated(defLocField.SourceTree, cache.GeneratedCodeMarkers)) return (findings, referenced);

        var (refCount, _) = await CheckSymbolReferencesAsync(field, project, solution, solutionProjectIds, isReferenceInSolutionSource, cache);

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocField != null ? SymbolUtilities.GetLinePosition(defLocField) : (-1, -1);
            Document? doc = defLocField != null ? solution.GetDocument(defLocField.SourceTree) : null;
            string projectDisplay = SymbolUtilities.BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = PathUtilities.GetDisplayPathForDocument(doc, defLocField?.SourceTree, project, solution);
            string displayName = PathUtilities.GetDisplayNameForDocument(doc, defLocField?.SourceTree);
            string fullPath = PathUtilities.GetFullPathForDocument(doc, defLocField?.SourceTree) ?? "(generated)";
            string projectFilePath = PathUtilities.GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = SymbolUtilities.GetIconForSymbolKind(SymbolKindField);

            findings.Add(new Finding
            {
                Project = projectDisplay,
                FilePath = fullPath,
                FilePathDisplay = filePathDisplay,
                DisplayName = displayName,
                ProjectFilePath = projectFilePath,
                Line = line,
                SymbolKind = "Field",
                ContainingType = type.ToDisplayString(),
                SymbolName = field.ToDisplayString(),
                Accessibility = field.DeclaredAccessibility.ToString(),
                Remarks = "No references found in solution source",
                DeclaredProject = declaredProject,
                FallbackProject = fallbackProject,
                Icon = icon,
                Confidence = CalculateConfidence("field", field.DeclaredAccessibility.ToString(), false, IsInTestFile(GetSyntaxTree(doc)), false)
            });
            EnrichFinding(findings[^1], field, false);
            progress?.Report($"    Unused field: {type.ToDisplayString()}.{field.Name} [{field.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }
        return (findings, referenced);
    }
}
