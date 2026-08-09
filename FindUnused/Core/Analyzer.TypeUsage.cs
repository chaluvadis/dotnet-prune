namespace FindUnused;

/// <summary>
/// Type usage analysis: AnalyzeTypeUsageAsync.
/// </summary>
public static partial class Analyzer
{
    /// <summary>
    /// Analyze type usage
    /// </summary>
    private static async Task<List<Finding>> AnalyzeTypeUsageAsync(
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        AnalyzerCache cache,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        // Consider the following kinds as types we want to detect as unused
        bool isRecord = type.IsRecord;
        bool considerType =
            type.TypeKind == TypeKind.Class ||
            type.TypeKind == TypeKind.Interface ||
            type.TypeKind == TypeKind.Enum ||
            type.TypeKind == TypeKind.Struct ||
            type.TypeKind == TypeKind.Delegate ||
            isRecord;
        if (!considerType) return findings;

        // Special handling for static classes: don't report as unused
        // Static classes are often used implicitly through their members
        if (type.IsStatic) return findings;

        // Consider only declaration locations that are not in excluded path folders
        var defTypeLocs = type.Locations.Where(l => l.IsInSource && !PathUtilities.SourceTreeIsExcluded(l.SourceTree, cache.ExclusionPatterns)).ToList();
        if (defTypeLocs.Count == 0) return findings;

        // For interfaces, additionally check for implementations in the solution
        bool foundUsage = false;
        if (type.TypeKind == TypeKind.Interface)
        {
            foundUsage = await CheckInterfaceImplementationsAsync(type, solution, isReferenceInSolutionSource, solutionProjectIds, cache);
            if (foundUsage) return findings;
        }

        // For classes, check derived classes (subclasses) inside the solution
        if (!foundUsage && type.TypeKind == TypeKind.Class)
        {
            foundUsage = await CheckDerivedClassesAsync(type, solution, isReferenceInSolutionSource, solutionProjectIds);
            if (foundUsage) return findings;
        }

        // General type references (variable declarations, cast, typeof, generics, attributes, etc.)
        var allTypeLocations = await cache.GetSymbolLocationsAsync(type, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        
        // O(N+M) instead of O(N*M): build a set of definition locations
        var defTypeSet = new HashSet<(Microsoft.CodeAnalysis.SyntaxTree? tree, Microsoft.CodeAnalysis.Text.TextSpan span)>(defTypeLocs.Select(d => (d.SourceTree, d.SourceSpan)));
        int typeRefCount = 0;
        foreach (var loc in allTypeLocations)
        {
            if (!defTypeSet.Contains((loc.SourceTree, loc.SourceSpan)))
                typeRefCount++;
        }

        // Fallback: do a manual semantic scan if SymbolFinder didn't find any references
        if (typeRefCount == 0)
        {
            var manualFound = await SemanticSearch.ManualSemanticSearchAsync(type, solution, solutionProjectIds);
            if (manualFound) typeRefCount = 1;
        }

        if (typeRefCount == 0)
        {
            var loc = defTypeLocs.FirstOrDefault();
            var (line, _) = loc != null ? SymbolUtilities.GetLinePosition(loc) : (-1, -1);
            var kind = isRecord ? "Record" : type.TypeKind.ToString();

            // Try to determine the project name from the declaration document if possible
            Document? doc = loc?.SourceTree != null ? solution.GetDocument(loc.SourceTree) : null;
            string projectDisplay = SymbolUtilities.BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = PathUtilities.GetDisplayPathForDocument(doc, loc?.SourceTree, project, solution);
            string displayName = PathUtilities.GetDisplayNameForDocument(doc, loc?.SourceTree);
            string fullPath = PathUtilities.GetFullPathForDocument(doc, loc?.SourceTree) ?? "(generated)";
            string projectFilePath = PathUtilities.GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = SymbolUtilities.GetIconForSymbolKind(SymbolKindType);

            findings.Add(new Finding
            {
                Project = projectDisplay,
                FilePath = fullPath,
                FilePathDisplay = filePathDisplay,
                DisplayName = displayName,
                ProjectFilePath = projectFilePath,
                Line = line,
                SymbolKind = "Type",
                ContainingType = type.ContainingType?.ToDisplayString() ?? "",
                SymbolName = type.ToDisplayString(),
                Accessibility = type.DeclaredAccessibility.ToString(),
                Remarks = $"No references found in solution source (TypeKind={kind})",
                DeclaredProject = declaredProject,
                FallbackProject = fallbackProject,
                Icon = icon,
                Confidence = CalculateConfidence(kind, type.DeclaredAccessibility.ToString(), false, IsInTestFile(GetSyntaxTree(doc)), false)
            });
            EnrichFinding(findings[^1], type, false);
            progress?.Report($"    Unused type: {type.ToDisplayString()} (Kind={kind}) [{type.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }

        return findings;
    }
}
