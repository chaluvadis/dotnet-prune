namespace FindUnused;

/// <summary>
/// Type-level analysis: AnalyzeTypeAsync and AnalyzeTypeMembersAsync.
/// </summary>
public static partial class Analyzer
{
    /// <summary>
    /// Analyze a type and its members
    /// </summary>
    private static async Task<(List<Finding> findings, bool typeHasReferencedMember)> AnalyzeTypeAsync(
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        HashSet<string> declaredNamespaces,
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
        bool typeHasReferencedMember = false;
        // Skip types outside the solution-declared namespaces
        if (!SymbolUtilities.IsNamespaceAllowed(type.ContainingNamespace, declaredNamespaces))
            return (findings, typeHasReferencedMember);
        if (type.IsImplicitlyDeclared) return (findings, typeHasReferencedMember);
        // Respect visibility options for types
        if (!ShouldAnalyzeType(type, includePublic, includeInternal)) return (findings, typeHasReferencedMember);

        // Consider only declaration locations that are not excluded (bin/obj/nuget/packages/debug)
        var defTypeLocs = type.Locations.Where(l => l.IsInSource && !PathUtilities.SourceTreeIsExcluded(l.SourceTree, cache.ExclusionPatterns)).ToList();
        if (defTypeLocs.Count == 0) return (findings, typeHasReferencedMember); // nothing in source to analyze (or all declarations excluded)

        // Analyze members first and record member-level usage
        var (memberFindings, hasReferencedMember) = await AnalyzeTypeMembersAsync(
            type, project, solution, declaredNamespaces, includePublic, includeInternal, excludeGenerated,
            compilation, solutionProjectIds, isReferenceInSolutionSource, cache, progress);
        findings.AddRange(memberFindings);
        typeHasReferencedMember = hasReferencedMember;

        return (findings, typeHasReferencedMember);
    }

    /// <summary>
    /// Analyze members of a type
    /// </summary>
    private static async Task<(List<Finding> findings, bool typeHasReferencedMember)> AnalyzeTypeMembersAsync(
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        HashSet<string> declaredNamespaces,
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
        bool typeHasReferencedMember = false;

        foreach (var member in type.GetMembers())
        {
            try
            {
                if (member.IsImplicitlyDeclared) continue;
                var defLoc = SymbolUtilities.GetSourceLocation(member);

                // Skip if the member's source file is in an excluded path
                if (defLoc != null && PathUtilities.SourceTreeIsExcluded(defLoc.SourceTree, cache.ExclusionPatterns)) continue;

                if (excludeGenerated && defLoc != null && SymbolUtilities.IsGenerated(defLoc.SourceTree, cache.GeneratedCodeMarkers)) continue;

                if (member is IMethodSymbol method)
                {
                    var (methodFindings, memberReferenced) = await AnalyzeMethodAsync(
                        method, type, project, solution, includePublic, includeInternal,
                        excludeGenerated, compilation, solutionProjectIds, isReferenceInSolutionSource, cache, progress);
                    findings.AddRange(methodFindings);
                    if (memberReferenced) typeHasReferencedMember = true;
                }
                else if (member is IPropertySymbol prop)
                {
                    var (propertyFindings, memberReferenced) = await AnalyzePropertyAsync(
                        prop, type, project, solution, includePublic, includeInternal,
                        excludeGenerated, solutionProjectIds, isReferenceInSolutionSource, cache, progress);
                    findings.AddRange(propertyFindings);
                    if (memberReferenced) typeHasReferencedMember = true;
                }
                else if (member is IFieldSymbol field)
                {
                    var (fieldFindings, memberReferenced) = await AnalyzeFieldAsync(
                        field, type, project, solution, includePublic, includeInternal,
                        excludeGenerated, solutionProjectIds, isReferenceInSolutionSource, cache, progress);
                    findings.AddRange(fieldFindings);
                    if (memberReferenced) typeHasReferencedMember = true;
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"    Warning analyzing member {member.Name}: {ex.Message}");
            }
        }
        return (findings, typeHasReferencedMember);
    }
}
