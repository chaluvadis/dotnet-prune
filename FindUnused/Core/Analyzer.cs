namespace FindUnused;

/// <summary>
/// Core analysis orchestration for finding unused symbols.
/// </summary>
public static partial class Analyzer
{
    // Constants for symbol kinds
    private const string SymbolKindMethod = "Method";
    private const string SymbolKindProperty = "Property";
    private const string SymbolKindField = "Field";
    private const string SymbolKindParameter = "Parameter";
    private const string SymbolKindType = "Type";

    /// <summary>
    /// Enrich a finding with confidence and severity
    /// </summary>
    private static void EnrichFinding(Finding finding, ISymbol symbol, bool hasReferences = false)
    {
        var confidence = FindingMetrics.CalculateConfidence(
            symbol,
            finding.Accessibility,
            finding.SymbolKind,
            hasReferences);

        finding.Confidence = confidence;
        finding.Severity = FindingMetrics.CalculateSeverity(
            finding.Accessibility,
            finding.SymbolKind,
            confidence);

        if (string.IsNullOrEmpty(finding.Icon))
        {
            finding.Icon = FindingMetrics.GetIconForSymbolKind(finding.SymbolKind);
        }
    }

    /// <summary>
    /// Analyze a single project for unused symbols
    /// </summary>
    public static async Task<List<Finding>> AnalyzeProjectAsync(
        Project project,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Dictionary<Project, List<INamedTypeSymbol>> projectDeclaredTypes,
        HashSet<string> declaredNamespaces,
        bool includePublic,
        bool includeInternal,
        bool excludeGenerated,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        AnalyzerCache cache,
        IProgress<string>? progress)
    {
        var projectFindings = new List<Finding>();
        progress?.Report($"\nAnalyzing project: {project.Name}");
        var compilation = await GetProjectCompilationAsync(project, progress);
        if (compilation == null) return projectFindings;
        var types = projectDeclaredTypes.TryGetValue(project, out var tlist) ? tlist : [];
        progress?.Report($"  Declared types found in project source: {types.Count}");
        foreach (var type in types)
        {
            try
            {
                var (typeFindings, typeHasReferencedMember) = await AnalyzeTypeAsync(
                    type,
                    project,
                    solution,
                    declaredNamespaces,
                    includePublic,
                    includeInternal,
                    excludeGenerated,
                    compilation,
                    solutionProjectIds,
                    isReferenceInSolutionSource,
                    cache,
                    progress);
                projectFindings.AddRange(typeFindings);
                // Only check type usage if no members were referenced
                if (!typeHasReferencedMember)
                {
                    var typeUsageFindings = await AnalyzeTypeUsageAsync(
                        type,
                        project,
                        solution,
                        solutionProjectIds,
                        isReferenceInSolutionSource,
                        cache,
                        progress);
                    projectFindings.AddRange(typeUsageFindings);
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"  Warning analyzing type {type.Name}: {ex.Message}");
            }
        }
        return projectFindings;
    }

    /// <summary>
    /// Get compilation for a project
    /// </summary>
    private static async Task<Compilation?> GetProjectCompilationAsync(Project project, IProgress<string>? progress)
    {
        try
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
            {
                progress?.Report($"  Could not get compilation for {project.Name}. Skipping.");
                return null;
            }
            return compilation;
        }
        catch (Exception ex)
        {
            progress?.Report($"  Compilation failed for {project.Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if type should be analyzed based on accessibility
    /// </summary>
    private static bool ShouldAnalyzeType(INamedTypeSymbol type, bool includePublic, bool includeInternal)
    {
        var tAcc = type.DeclaredAccessibility;
        if (tAcc == Accessibility.Public && !includePublic) return false;
        if (tAcc == Accessibility.Internal && !includeInternal) return false;
        if (tAcc == Accessibility.Protected || tAcc == Accessibility.ProtectedOrInternal) return false;
        return true;
    }
}
