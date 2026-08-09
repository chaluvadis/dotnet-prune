namespace FindUnused;

/// <summary>
/// Reference checking: CheckSymbolReferencesAsync, CheckInterfaceReferencesAsync,
/// CheckInterfaceImplementationsAsync, CheckDerivedClassesAsync, GetImplementedInterfaceMethod.
/// </summary>
public static partial class Analyzer
{
    /// <summary>
    /// Check for interface implementations
    /// </summary>
    private static async Task<bool> CheckInterfaceImplementationsAsync(
        INamedTypeSymbol type,
        Solution solution,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        HashSet<ProjectId> solutionProjectIds,
        AnalyzerCache cache)
    {
        try
        {
            var impls = await SymbolFinder.FindImplementationsAsync(type, solution);
            foreach (var impl in impls)
            {
                foreach (var loc in impl.Locations)
                {
                    if (isReferenceInSolutionSource(loc, solution, solutionProjectIds))
                        return true;
                }
                if (impl is INamedTypeSymbol nt)
                {
                    var ntDefLocs = nt.Locations.Where(l => l.IsInSource && !PathUtilities.SourceTreeIsExcluded(l.SourceTree, cache.ExclusionPatterns));
                    if (ntDefLocs.Any(l => isReferenceInSolutionSource(l, solution, solutionProjectIds)))
                        return true;
                }
            }
        }
        catch
        {
            // If API not available, ignore and return false
        }
        return false;
    }

    /// <summary>
    /// Check for derived classes
    /// </summary>
    private static async Task<bool> CheckDerivedClassesAsync(
        INamedTypeSymbol type,
        Solution solution,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        HashSet<ProjectId> solutionProjectIds)
    {
        try
        {
            var derived = await SymbolFinder.FindDerivedClassesAsync(type, solution);
            foreach (var d in derived)
            {
                foreach (var loc in d.Locations)
                {
                    if (isReferenceInSolutionSource(loc, solution, solutionProjectIds))
                        return true;
                }
            }
        }
        catch
        {
            // If API not available (older Roslyn), ignore and return false
        }
        return false;
    }

    /// <summary>
    /// Get the interface method that this method implements, if any
    /// </summary>
    private static IMethodSymbol? GetImplementedInterfaceMethod(IMethodSymbol method, INamedTypeSymbol containingType)
    {
        // Check explicit implementations
        if (method.ExplicitInterfaceImplementations.Any())
        {
            return method.ExplicitInterfaceImplementations.First();
        }

        // Check implicit implementations
        foreach (var iface in containingType.AllInterfaces)
        {
            var candidates = iface.GetMembers().OfType<IMethodSymbol>()
                .Where(m => m.Name == method.Name &&
                           SymbolEqualityComparer.Default.Equals(m.ReturnType, method.ReturnType) &&
                           m.Parameters.Length == method.Parameters.Length &&
                           m.Parameters.Zip(method.Parameters, (p1, p2) => SymbolEqualityComparer.Default.Equals(p1.Type, p2.Type)).All(x => x));
            if (candidates.Any())
            {
                return candidates.First();
            }
        }

        return null;
    }

    /// <summary>
    /// Check for interface method implementations
    /// </summary>
    private static async Task<int> CheckInterfaceReferencesAsync(
        IMethodSymbol method,
        INamedTypeSymbol type,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource)
    {
        int refCount = 0;
        var interfaceMethod = GetImplementedInterfaceMethod(method, type);
        if (interfaceMethod != null)
        {
            var interfaceRefs = await SymbolFinder.FindReferencesAsync(interfaceMethod, solution);
            foreach (var rr in interfaceRefs)
            {
                foreach (var loc in rr.Locations)
                {
                    if (!isReferenceInSolutionSource(loc.Location, solution, solutionProjectIds)) continue;
                    bool isDefinitionLocation = interfaceMethod.Locations.Where(l => l.IsInSource).Any(d =>
                        d.SourceTree == loc.Location.SourceTree &&
                        d.SourceSpan.Equals(loc.Location.SourceSpan));
                    if (!isDefinitionLocation) refCount++;
                }
            }
        }
        return refCount;
    }

    /// <summary>
    /// Common method to check symbol references.
    /// Uses O(N+M) definition-set lookup instead of O(N*M) nested enumeration.
    /// </summary>
    private static async Task<(int refCount, bool referenced)> CheckSymbolReferencesAsync(
        ISymbol symbol,
        Project project,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        AnalyzerCache cache)
    {
        // Build a set of definition locations for O(1) lookup
        var defSet = new HashSet<(Microsoft.CodeAnalysis.SyntaxTree? tree, Microsoft.CodeAnalysis.Text.TextSpan span)>(
            symbol.Locations.Where(l => l.IsInSource).Select(d => (d.SourceTree, d.SourceSpan)));
        
        var allLocations = await cache.GetSymbolLocationsAsync(symbol, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        int refCount = 0;
        foreach (var loc in allLocations)
        {
            if (!defSet.Contains((loc.SourceTree, loc.SourceSpan)))
                refCount++;
        }

        // Fallback: manual semantic search
        if (refCount == 0)
        {
            if (await SemanticSearch.ManualSemanticSearchAsync(symbol, solution, solutionProjectIds))
                refCount = 1;
        }

        bool referenced = refCount > 0;
        return (refCount, referenced);
    }
}
