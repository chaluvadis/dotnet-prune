namespace FindUnused;

/// <summary>
/// Configuration options for the analyzer
/// </summary>
public class AnalyzerConfiguration
{
    public bool IncludePublicSymbols { get; set; } = true;
    public bool IncludeInternalSymbols { get; set; } = true;
    public bool ExcludeGeneratedCode { get; set; } = true;
}

/// <summary>
/// Cache for project analysis results
/// </summary>
public class ProjectAnalysisCache
{
    public Dictionary<ISymbol, HashSet<Location>> SymbolLocations { get; }
        = new Dictionary<ISymbol, HashSet<Location>>(SymbolEqualityComparer.Default);
}

/// <summary>
/// Intelligent caching system for analyzer performance
/// </summary>
public class AnalyzerCache
{
    private readonly Dictionary<ProjectId, ProjectAnalysisCache> _projectCaches = [];

    private ProjectAnalysisCache GetProjectCache(ProjectId projectId)
    {
        if (!_projectCaches.TryGetValue(projectId, out var cache))
        {
            cache = new ProjectAnalysisCache();
            _projectCaches[projectId] = cache;
        }
        return cache;
    }

    public async Task<HashSet<Location>> GetSymbolLocationsAsync(
        ISymbol symbol,
        ProjectId projectId,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource)
    {
        var cache = GetProjectCache(projectId);
        if (cache.SymbolLocations.TryGetValue(symbol, out var locations))
            return locations;

        // Perform search and cache results
        locations = await PerformSymbolSearchAsync(symbol, solution, solutionProjectIds, isReferenceInSolutionSource);
        cache.SymbolLocations[symbol] = locations;
        return locations;
    }

    private static async Task<HashSet<Location>> PerformSymbolSearchAsync(
        ISymbol symbol,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource
    )
    {
        var locations = new HashSet<Location>();
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
        foreach (var rr in references)
        {
            foreach (var loc in rr.Locations)
            {
                if (isReferenceInSolutionSource(loc.Location, solution, solutionProjectIds))
                {
                    locations.Add(loc.Location);
                }
            }
        }
        return locations;
    }
}
