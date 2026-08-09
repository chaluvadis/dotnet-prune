namespace FindUnused;

/// <summary>
/// Method analysis: AnalyzeMethodAsync and AnalyzeMethodParametersAsync.
/// </summary>
public static partial class Analyzer
{
    /// <summary>
    /// Analyze method usage
    /// </summary>
    private static async Task<(List<Finding> findings, bool referenced)> AnalyzeMethodAsync(
        IMethodSymbol method,
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        bool includePublic,
        bool includeInternal,
        bool excludeGenerated,
        Compilation compilation,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        AnalyzerCache cache,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool referenced = false;
        // Skip various method types that we don't want to analyze
        if (method.MethodKind == MethodKind.PropertyGet || method.MethodKind == MethodKind.PropertySet) return (findings, referenced);
        if (method.MethodKind == MethodKind.EventAdd || method.MethodKind == MethodKind.EventRemove) return (findings, referenced);
        if (method.MethodKind == MethodKind.StaticConstructor || method.MethodKind == MethodKind.Constructor) return (findings, referenced);
        if (method.IsOverride || method.ExplicitInterfaceImplementations.Any()) return (findings, referenced);

        // Special handling for extension methods: consider them as potentially used
        if (method.IsExtensionMethod) return (findings, true);

        var acc = method.DeclaredAccessibility;
        if (acc == Accessibility.Public && !includePublic) return (findings, referenced);
        if (acc == Accessibility.Internal && !includeInternal) return (findings, referenced);
        if (acc == Accessibility.Protected || acc == Accessibility.ProtectedOrInternal) return (findings, referenced);
        var entry = compilation.GetEntryPoint(CancellationToken.None);
        if (entry != null && SymbolEqualityComparer.Default.Equals(entry, method)) return (findings, referenced);
        // Skip entry point methods as they are not considered unused
        if (SymbolUtilities.IsEntryPointMethod(method, type, cache.TestAttributes, cache.TestClassAttributes))
        {
            return (findings, true); // Return referenced=true to indicate it's an entry point
        }
        var defLoc = SymbolUtilities.GetSourceLocation(method);

        // Skip if definition is in an excluded folder
        if (defLoc != null && PathUtilities.SourceTreeIsExcluded(defLoc.SourceTree, cache.ExclusionPatterns)) return (findings, referenced);

        if (excludeGenerated && defLoc != null && SymbolUtilities.IsGenerated(defLoc.SourceTree, cache.GeneratedCodeMarkers)) return (findings, referenced);

        // Check direct references
        var (refCount, _) = await CheckSymbolReferencesAsync(method, project, solution, solutionProjectIds, isReferenceInSolutionSource, cache);

        // If no direct references found, check interface implementations
        if (refCount == 0)
        {
            refCount = await CheckInterfaceReferencesAsync(method, type, solution, solutionProjectIds, isReferenceInSolutionSource);
        }

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLoc != null ? SymbolUtilities.GetLinePosition(defLoc) : (-1, -1);
            Document? doc = defLoc != null ? solution.GetDocument(defLoc.SourceTree) : null;
            string projectDisplay = SymbolUtilities.BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = PathUtilities.GetDisplayPathForDocument(doc, defLoc?.SourceTree, project, solution);
            string displayName = PathUtilities.GetDisplayNameForDocument(doc, defLoc?.SourceTree);
            string fullPath = PathUtilities.GetFullPathForDocument(doc, defLoc?.SourceTree) ?? "(generated)";
            string projectFilePath = PathUtilities.GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = SymbolUtilities.GetIconForSymbolKind(SymbolKindMethod);

            findings.Add(new Finding
            {
                Project = projectDisplay,
                FilePath = fullPath,
                FilePathDisplay = filePathDisplay,
                DisplayName = displayName,
                ProjectFilePath = projectFilePath,
                Line = line,
                SymbolKind = SymbolKindMethod,
                ContainingType = type.ToDisplayString(),
                SymbolName = method.ToDisplayString(),
                Accessibility = method.DeclaredAccessibility.ToString(),
                Remarks = "No references found in solution source",
                DeclaredProject = declaredProject,
                FallbackProject = fallbackProject,
                Icon = icon,
                Confidence = CalculateConfidence(SymbolKindMethod, method.DeclaredAccessibility.ToString(), false, IsInTestFile(GetSyntaxTree(doc)), method.ExplicitInterfaceImplementations.Any())
            });
            EnrichFinding(findings[^1], method, false);
            progress?.Report($"    Unused method: {type.ToDisplayString()}.{method.Name} [{method.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }
        // Analyze method parameters
        var parameterFindings = await AnalyzeMethodParametersAsync(method, type, project, solution, solutionProjectIds, isReferenceInSolutionSource, cache, progress);
        findings.AddRange(parameterFindings);
        return (findings, referenced);
    }

    /// <summary>
    /// Analyze method parameters for unused parameters
    /// </summary>
    private static async Task<List<Finding>> AnalyzeMethodParametersAsync(
        IMethodSymbol method,
        INamedTypeSymbol type,
        Project? project,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        AnalyzerCache cache,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        foreach (var param in method.Parameters)
        {
            if (param.RefKind != RefKind.None) continue;
            var paramRefs = await SymbolFinder.FindReferencesAsync(param, solution);
            var paramDefLocs = param.Locations.Where(l => l.IsInSource && !PathUtilities.SourceTreeIsExcluded(l.SourceTree, cache.ExclusionPatterns)).ToList();
            
            // Build a HashSet of definition locations for O(1) lookup
            var paramDefSet = new HashSet<(Microsoft.CodeAnalysis.SyntaxTree? tree, Microsoft.CodeAnalysis.Text.TextSpan span)>(paramDefLocs.Select(d => (d.SourceTree, d.SourceSpan)));
            int paramRefCount = 0;
            foreach (var rr in paramRefs)
            {
                foreach (var loc in rr.Locations)
                {
                    if (!isReferenceInSolutionSource(loc.Location, solution, solutionProjectIds)) continue;
                    if (!paramDefSet.Contains((loc.Location.SourceTree, loc.Location.SourceSpan))) paramRefCount++;
                }
            }
            
            if (paramRefCount == 0)
            {
                var pLoc = paramDefLocs.FirstOrDefault();
                var (pline, _) = pLoc != null ? SymbolUtilities.GetLinePosition(pLoc) : (-1, -1);
                Document? doc = pLoc != null ? solution.GetDocument(pLoc.SourceTree) : null;
                string projectDisplay = SymbolUtilities.BuildProjectDisplayNameFrom(project, doc);
                string filePathDisplay = PathUtilities.GetDisplayPathForDocument(doc, pLoc?.SourceTree, project, solution);
                string displayName = PathUtilities.GetDisplayNameForDocument(doc, pLoc?.SourceTree);
                string fullPath = PathUtilities.GetFullPathForDocument(doc, pLoc?.SourceTree) ?? "(generated)";
                string projectFilePath = PathUtilities.GetProjectFilePath(project, doc) ?? "(unknown)";
                string declaredProject = doc?.Project?.Name ?? "(null)";
                string fallbackProject = project?.Name ?? "(null)";
                string icon = SymbolUtilities.GetIconForSymbolKind(SymbolKindParameter);

                findings.Add(new Finding
                {
                    Project = projectDisplay,
                    FilePath = fullPath,
                    FilePathDisplay = filePathDisplay,
                    DisplayName = displayName,
                    ProjectFilePath = projectFilePath,
                    Line = pline,
                    SymbolKind = "Parameter",
                    ContainingType = type.ToDisplayString(),
                    SymbolName = $"{method.ToDisplayString()} :: {param.Name}",
                    Accessibility = method.DeclaredAccessibility.ToString(),
                    Remarks = "Parameter never referenced in solution source",
                    DeclaredProject = declaredProject,
                    FallbackProject = fallbackProject,
                    Icon = icon,
                    Confidence = CalculateConfidence("parameter", method.DeclaredAccessibility.ToString(), true, IsInTestFile(GetSyntaxTree(doc)), false)
                });
                EnrichFinding(findings[^1], param, false);
                progress?.Report($"      Unused parameter: {method.ToDisplayString()} :: {param.Name} at {filePathDisplay}:{pline}");
            }
        }
        return findings;
    }
}
