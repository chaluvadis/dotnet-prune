namespace FindUnused;

/// <summary>
/// Configuration options for the analyzer
/// </summary>
public class AnalyzerConfiguration
{
    public bool IncludePublicSymbols { get; set; } = true;
    public bool IncludeInternalSymbols { get; set; } = true;
    public bool ExcludeGeneratedCode { get; set; } = true;
    public bool EnableReflectionDetection { get; set; } = true;
    public bool EnableCrossProjectAnalysis { get; set; } = true;
    public bool EnableEntryPointDetection { get; set; } = true;
    public HashSet<string> AdditionalEntryPointPatterns { get; set; } = new();
    public HashSet<string> CustomAttributesToIgnore { get; set; } = new();
    public int MaxConcurrencyLevel { get; set; } = Environment.ProcessorCount;
}

/// <summary>
/// Cache for project analysis results
/// </summary>
public class ProjectAnalysisCache
{
    public Dictionary<ISymbol, HashSet<Location>> SymbolLocations { get; } = new Dictionary<ISymbol, HashSet<Location>>(SymbolEqualityComparer.Default);
}

/// <summary>
/// Cache for document analysis results
/// </summary>
public class DocumentAnalysisCache
{
    // Placeholder for document-level caching
}

/// <summary>
/// Intelligent caching system for analyzer performance
/// </summary>
public class AnalyzerCache
{
    private readonly Dictionary<ProjectId, ProjectAnalysisCache> _projectCaches = new();
    private readonly Dictionary<DocumentId, DocumentAnalysisCache> _documentCaches = new();

    private ProjectAnalysisCache GetProjectCache(ProjectId projectId)
    {
        if (!_projectCaches.TryGetValue(projectId, out var cache))
        {
            cache = new ProjectAnalysisCache();
            _projectCaches[projectId] = cache;
        }
        return cache;
    }

    public async Task<HashSet<Location>> GetSymbolLocationsAsync(ISymbol symbol, ProjectId projectId, Solution solution, HashSet<ProjectId> solutionProjectIds, Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource)
    {
        var cache = GetProjectCache(projectId);
        if (cache.SymbolLocations.TryGetValue(symbol, out var locations))
            return locations;

        // Perform search and cache results
        locations = await PerformSymbolSearchAsync(symbol, solution, solutionProjectIds, isReferenceInSolutionSource);
        cache.SymbolLocations[symbol] = locations;
        return locations;
    }

    private async Task<HashSet<Location>> PerformSymbolSearchAsync(ISymbol symbol, Solution solution, HashSet<ProjectId> solutionProjectIds, Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource)
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

/// <summary>
/// Result object for analysis operations
/// </summary>
public record AnalysisResult
{
    public bool Success { get; set; }
    public List<Finding> Findings { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public int FindingsCount => Findings.Count;
}

/// <summary>
/// Represents a finding of unused code
/// </summary>
public record Finding
{
    // Project display "Name (path)"
    public string Project { get; set; } = string.Empty;

    // FilePath now contains the full absolute path to the file (for extension-level navigation)
    public string FilePath { get; set; } = string.Empty;

    // FilePathDisplay is the project/solution-relative display path (human readable)
    public string FilePathDisplay { get; set; } = string.Empty;

    // New DisplayName intended for quick UI display: filename only (as shown in Solution Explorer)
    public string DisplayName { get; set; } = string.Empty;

    // New ProjectFilePath: absolute path to the project (.csproj) that contains the file
    public string ProjectFilePath { get; set; } = string.Empty;

    public int Line { get; set; }
    public string SymbolKind { get; set; } = string.Empty;
    public string ContainingType { get; set; } = string.Empty;
    public string SymbolName { get; set; } = string.Empty;
    public string Accessibility { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;

    // Diagnostic fields embedded into JSON per finding
    public string DeclaredProject { get; set; } = string.Empty;
    public string FallbackProject { get; set; } = string.Empty;

    // Icon for quick visual identification
    public string Icon { get; set; } = string.Empty;
}

/// <summary>
/// Main analysis class for finding unused C# code symbols
/// </summary>
public class FindUnusedAnalyzer
{
    // Diagnostic mode: when true, prints extra diagnostic information per finding
    public static bool DiagnosticMode { get; set; } = false;

    private static readonly AnalyzerCache _cache = new();

    private static JsonSerializerOptions GetOptions() => new() { WriteIndented = true };

    /// <summary>
    /// Run the analysis with specified parameters
    /// </summary>
    /// <param name="targetPath">Path to .slnx, .sln, .csproj file or folder to analyze</param>
    /// <param name="config">Configuration options for the analysis</param>
    /// <param name="progress">Optional progress reporter for UI updates</param>
    /// <returns>Analysis results</returns>
    public static async Task<AnalysisResult> RunAnalysisAsync(
        string targetPath,
        AnalyzerConfiguration? config = null,
        IProgress<string>? progress = null)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new AnalysisResult
            {
                Success = false,
                ErrorMessage = "Target path is required"
            };
        }
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return new AnalysisResult
            {
                Success = false,
                ErrorMessage = $"Target '{targetPath}' not found"
            };
        }
        var analysisConfig = config ?? new AnalyzerConfiguration();
        var findings = new List<Finding>();
        try
        {
            // Setup workspace and load solution
            progress?.Report($"Opening '{targetPath}'...");
            var (solution, solutionProjectIds) = await SetupWorkspaceAsync(targetPath, progress);
            if (solution == null)
            {
                return new AnalysisResult
                {
                    Success = false,
                    ErrorMessage = "Failed to load solution"
                };
            }
            // Get declared namespaces and build types map
            var declaredNamespaces = await GetDeclaredNamespacesFromSolutionAsync(solution);
            progress?.Report($"Declared namespaces found by syntax scan: {declaredNamespaces.Count}");
            var projectDeclaredTypes = await BuildProjectDeclaredTypesMapAsync(solution, declaredNamespaces);
            progress?.Report($"Declared namespaces after augmentation: {declaredNamespaces.Count}");
            // Analyze each project in parallel
            if (solutionProjectIds != null)
            {
                var projectTasks = solution.Projects.Select(project => AnalyzeProjectAsync(
                    project,
                    solution,
                    solutionProjectIds,
                    projectDeclaredTypes,
                    declaredNamespaces,
                    analysisConfig.IncludePublicSymbols,
                    analysisConfig.IncludeInternalSymbols,
                    analysisConfig.ExcludeGeneratedCode,
                    IsReferenceInSolutionSource,
                    progress));
                var projectFindingsArrays = await Task.WhenAll(projectTasks);
                foreach (var arr in projectFindingsArrays)
                {
                    findings.AddRange(arr);
                }
            }
            return new AnalysisResult
            {
                Success = true,
                Findings = findings
            };
        }
        catch (Exception ex)
        {
            return new AnalysisResult
            {
                Success = false,
                ErrorMessage = $"Analysis failed: {ex.Message}",
                Findings = findings
            };
        }
    }
    private static bool IsReferenceInSolutionSource(Location loc, Solution solution, HashSet<ProjectId> solutionProjectIds)
    {
        if (loc == null || !loc.IsInSource) return false;
        var doc = solution.GetDocument(loc.SourceTree);
        return doc != null && solutionProjectIds.Contains(doc.Project.Id);
    }

    private static async Task<(Solution? solution, HashSet<ProjectId>? projectIds)> SetupWorkspaceAsync(string targetPath, IProgress<string>? progress)
    {
        MSBuildLocator.RegisterDefaults();
        using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>());
        using var workspaceFailedRegistration = workspace.RegisterWorkspaceFailedHandler(diagnostic =>
        {
            progress?.Report($"Workspace warning: {diagnostic}");
        });
        var solution = await LoadSolutionFromPath(targetPath, workspace);
        if (solution == null) return (null, null);
        progress?.Report($"Loaded solution: {solution.FilePath ?? "(in-memory)"}");
        progress?.Report($"Projects: {solution.Projects.Count()}");
        var solutionProjectIds = new HashSet<ProjectId>(solution.Projects.Select(p => p.Id));

        // Expand to include referenced projects for cross-project analysis
        var expandedIds = new HashSet<ProjectId>(solutionProjectIds);
        foreach (var projectId in solutionProjectIds)
        {
            var proj = solution.GetProject(projectId);
            if (proj?.ProjectReferences != null)
            {
                foreach (var projectRef in proj.ProjectReferences)
                {
                    expandedIds.Add(projectRef.ProjectId);
                }
            }
        }
        solutionProjectIds = expandedIds;

        return (solution, solutionProjectIds);
    }

    /// <summary>
    /// Determine if the given file path is located inside an excluded folder (NuGet/global packages, bin, obj, debug).
    /// This is a lightweight textual check intentionally tolerant for cross-platform separators.
    /// </summary>
    private static bool IsPathExcluded(string? filePath)
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

        return indicators.Any(ind => low.Contains(ind));
    }

    private static bool DocumentIsExcluded(Document? doc)
    {
        if (doc == null) return false;
        return IsPathExcluded(doc.FilePath);
    }

    private static bool SourceTreeIsExcluded(SyntaxTree? tree)
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
    private static string GetDisplayPathForDocument(Document? doc, SyntaxTree? tree, Project? project, Solution? solution)
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
    private static string GetDisplayNameForDocument(Document? doc, SyntaxTree? tree)
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
    private static string? GetFullPathForDocument(Document? doc, SyntaxTree? tree)
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
    private static string? GetProjectFilePath(Project? project, Document? doc)
    {
        // prefer project.FilePath, else try doc.Project.FilePath
        if (project?.FilePath != null) return Path.GetFullPath(project.FilePath);
        if (doc?.Project?.FilePath != null) return Path.GetFullPath(doc.Project.FilePath);
        return null;
    }

    private static async Task<Dictionary<Project, List<INamedTypeSymbol>>> BuildProjectDeclaredTypesMapAsync(
        Solution solution,
        HashSet<string> declaredNamespaces)
    {
        var projectDeclaredTypes = new Dictionary<Project, List<INamedTypeSymbol>>();
        foreach (var project in solution.Projects)
        {
            var list = await GetDeclaredTypesInProjectAsync(project);
            projectDeclaredTypes[project] = list;
            // Augment declaredNamespaces with the namespaces of these types
            foreach (var type in list)
            {
                var ns = type.ContainingNamespace?.ToDisplayString();
                if (string.IsNullOrEmpty(ns))
                {
                    declaredNamespaces.Add(string.Empty);
                }
                else
                {
                    // add all parent namespace prefixes for looser matching
                    var parts = ns.Split('.');
                    for (int i = 1; i <= parts.Length; i++)
                    {
                        var prefix = string.Join(".", parts.Take(i));
                        declaredNamespaces.Add(prefix);
                    }
                }
            }
        }
        return projectDeclaredTypes;
    }

    private static async Task<List<Finding>> AnalyzeProjectAsync(
        Project project,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Dictionary<Project, List<INamedTypeSymbol>> projectDeclaredTypes,
        HashSet<string> declaredNamespaces,
        bool includePublic,
        bool includeInternal,
        bool excludeGenerated,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
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
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool typeHasReferencedMember = false;
        // Skip types outside the solution-declared namespaces
        if (!IsNamespaceAllowed(type.ContainingNamespace, declaredNamespaces))
            return (findings, typeHasReferencedMember);
        if (type.IsImplicitlyDeclared) return (findings, typeHasReferencedMember);
        // Respect visibility options for types
        var tAcc = type.DeclaredAccessibility;
        if (tAcc == Accessibility.Public && !includePublic) return (findings, typeHasReferencedMember);
        if (tAcc == Accessibility.Internal && !includeInternal && tAcc != Accessibility.Private) return (findings, typeHasReferencedMember);
        if (tAcc == Accessibility.Protected || tAcc == Accessibility.ProtectedOrInternal) return (findings, typeHasReferencedMember);

        // Consider only declaration locations that are not excluded (bin/obj/nuget/packages/debug)
        var defTypeLocs = type.Locations.Where(l => l.IsInSource && !SourceTreeIsExcluded(l.SourceTree)).ToList();
        if (defTypeLocs.Count == 0) return (findings, typeHasReferencedMember); // nothing in source to analyze (or all declarations excluded)

        // Analyze members first and record member-level usage
        foreach (var member in type.GetMembers())
        {
            try
            {
                if (member.IsImplicitlyDeclared) continue;
                var defLoc = GetSourceLocation(member);

                // Skip if the member's source file is in an excluded path
                if (defLoc != null && SourceTreeIsExcluded(defLoc.SourceTree)) continue;

                if (excludeGenerated && defLoc != null && IsGenerated(defLoc.SourceTree)) continue;

                if (member is IMethodSymbol method)
                {
                    var (methodFindings, memberReferenced) = await AnalyzeMethodAsync(
                        method, type, project, solution, includePublic, includeInternal,
                        excludeGenerated, compilation, solutionProjectIds, isReferenceInSolutionSource, progress);
                    findings.AddRange(methodFindings);
                    if (memberReferenced) typeHasReferencedMember = true;
                }
                else if (member is IPropertySymbol prop)
                {
                    var (propertyFindings, memberReferenced) = await AnalyzePropertyAsync(
                        prop, type, project, solution, includeInternal, includePublic,
                        excludeGenerated, solutionProjectIds, isReferenceInSolutionSource, progress);
                    findings.AddRange(propertyFindings);
                    if (memberReferenced) typeHasReferencedMember = true;
                }
                else if (member is IFieldSymbol field)
                {
                    var (fieldFindings, memberReferenced) = await AnalyzeFieldAsync(
                        field, type, project, solution, includeInternal, includePublic,
                        excludeGenerated, solutionProjectIds, isReferenceInSolutionSource, progress);
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

    private static string BuildProjectDisplayNameFrom(Project? fallbackProject, Document? doc)
    {
        var name = doc?.Project?.Name ?? fallbackProject?.Name ?? "Unknown";
        var path = doc?.Project?.FilePath ?? fallbackProject?.FilePath ?? "(unknown)";
        return $"{name} ({path})";
    }

    private static string GetIconForSymbolKind(string symbolKind)
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
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool referenced = false;
        // Skip various method types that we don't want to analyze
        if (method.MethodKind == MethodKind.PropertyGet || method.MethodKind == MethodKind.PropertySet) return (findings, referenced);
        if (method.MethodKind == MethodKind.EventAdd || method.MethodKind == MethodKind.EventRemove) return (findings, referenced);
        if (method.MethodKind == MethodKind.StaticConstructor || method.MethodKind == MethodKind.Constructor) return (findings, referenced);
        if (method.IsOverride || method.ExplicitInterfaceImplementations.Any()) return (findings, referenced);
        var acc = method.DeclaredAccessibility;
        if (acc == Accessibility.Public && !includePublic) return (findings, referenced);
        if (acc == Accessibility.Internal && !includeInternal && acc != Accessibility.Private) return (findings, referenced);
        if (acc == Accessibility.Protected || acc == Accessibility.ProtectedOrInternal) return (findings, referenced);
        var entry = compilation.GetEntryPoint(CancellationToken.None);
        if (entry != null && SymbolEqualityComparer.Default.Equals(entry, method)) return (findings, referenced);
        // Skip entry point methods as they are not considered unused
        if (IsEntryPointMethod(method, type))
        {
            return (findings, false); // Return referenced=true to indicate it's an entry point
        }
        var defLoc = GetSourceLocation(method);

        // Skip if definition is in an excluded folder
        if (defLoc != null && SourceTreeIsExcluded(defLoc.SourceTree)) return (findings, referenced);

        if (excludeGenerated && defLoc != null && IsGenerated(defLoc.SourceTree)) return (findings, referenced);
        // Find references across the solution using cache
        var defLocations = method.Locations.Where(l => l.IsInSource).ToList();
        var allLocations = await _cache.GetSymbolLocationsAsync(method, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        int refCount = allLocations.Count(l => !defLocations.Any(d =>
            d.SourceTree == l.SourceTree &&
            d.SourceSpan.Equals(l.SourceSpan)));

        // If no direct references found, check if this method implements an interface method
        if (refCount == 0)
        {
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
        }

        // Fallback: manual semantic search for all symbols
        if (refCount == 0)
        {
            if (await ManualSemanticSearchForAllSymbolsAsync(method, solution, solutionProjectIds))
                refCount = 1;
        }

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLoc != null ? GetLinePosition(defLoc) : (-1, -1);
            Document? doc = defLoc != null ? solution.GetDocument(defLoc.SourceTree) : null;
            string projectDisplay = BuildProjectDisplayNameFrom(project, doc);

            // File path displayed relative to project/solution or using virtual folder structure in Project
            string filePathDisplay = GetDisplayPathForDocument(doc, defLoc?.SourceTree, project, solution);
            string displayName = GetDisplayNameForDocument(doc, defLoc?.SourceTree);

            // Full path for extension-level use
            string fullPath = GetFullPathForDocument(doc, defLoc?.SourceTree) ?? "(generated)";

            // Project file path (absolute) for extension-level use
            string projectFilePath = GetProjectFilePath(project, doc) ?? "(unknown)";

            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = GetIconForSymbolKind("Method");

            // Diagnostic logging when doc==null
            if (DiagnosticMode)
            {
                progress?.Report($"[Diagnostic] Method finding: declaredProject={declaredProject}, fallbackProject={fallbackProject}, declarationFileDisplay={filePathDisplay}, fullPath={fullPath}, projectFile={projectFilePath}");
                if (doc == null)
                    progress?.Report($"[Diagnostic] declaration document for method not found; falling back to project.Name '{fallbackProject}'");
            }

            findings.Add(new Finding
            {
                Project = projectDisplay,
                FilePath = fullPath,
                FilePathDisplay = filePathDisplay,
                DisplayName = displayName,
                ProjectFilePath = projectFilePath,
                Line = line,
                SymbolKind = "Method",
                ContainingType = type.ToDisplayString(),
                SymbolName = method.ToDisplayString(),
                Accessibility = method.DeclaredAccessibility.ToString(),
                Remarks = "No references found in solution source",
                DeclaredProject = declaredProject,
                FallbackProject = fallbackProject,
                Icon = icon
            });
            progress?.Report($"    Unused method: {type.ToDisplayString()}.{method.Name} [{method.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }
        // Analyze method parameters
        var parameterFindings = await AnalyzeMethodParametersAsync(method, type, project, solution, solutionProjectIds, isReferenceInSolutionSource, progress);
        findings.AddRange(parameterFindings);
        return (findings, referenced);
    }

    private static async Task<List<Finding>> AnalyzeMethodParametersAsync(
        IMethodSymbol method,
        INamedTypeSymbol type,
        Project? project,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        foreach (var param in method.Parameters)
        {
            if (param.RefKind != RefKind.None) continue;
            var paramRefs = await SymbolFinder.FindReferencesAsync(param, solution);
            var paramDefLocs = param.Locations.Where(l => l.IsInSource && !SourceTreeIsExcluded(l.SourceTree)).ToList();
            int paramRefCount = 0;
            foreach (var rr in paramRefs)
            {
                foreach (var loc in rr.Locations)
                {
                    if (!isReferenceInSolutionSource(loc.Location, solution, solutionProjectIds)) continue;
                    bool isDefLoc = paramDefLocs.Any(d => d.SourceTree == loc.Location.SourceTree && d.SourceSpan.Equals(loc.Location.SourceSpan));
                    if (!isDefLoc) paramRefCount++;
                }
            }
            if (paramRefCount == 0)
            {
                var pLoc = paramDefLocs.FirstOrDefault();
                var (pline, _) = pLoc != null ? GetLinePosition(pLoc) : (-1, -1);
                Document? doc = pLoc != null ? solution.GetDocument(pLoc.SourceTree) : null;
                string projectDisplay = BuildProjectDisplayNameFrom(project, doc);
                string filePathDisplay = GetDisplayPathForDocument(doc, pLoc?.SourceTree, project, solution);
                string displayName = GetDisplayNameForDocument(doc, pLoc?.SourceTree);
                string fullPath = GetFullPathForDocument(doc, pLoc?.SourceTree) ?? "(generated)";
                string projectFilePath = GetProjectFilePath(project, doc) ?? "(unknown)";
                string declaredProject = doc?.Project?.Name ?? "(null)";
                string fallbackProject = project?.Name ?? "(null)";
                string icon = GetIconForSymbolKind("Parameter");

                if (DiagnosticMode)
                {
                    progress?.Report($"[Diagnostic] Parameter finding: declaredProject={declaredProject}, fallbackProject={fallbackProject}, declarationFileDisplay={filePathDisplay}, fullPath={fullPath}, projectFile={projectFilePath}");
                    if (doc == null)
                        progress?.Report($"[Diagnostic] declaration document for parameter not found; falling back to project.Name '{fallbackProject}'");
                }

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
                    Icon = icon
                });
                progress?.Report($"      Unused parameter: {method.ToDisplayString()} :: {param.Name} at {filePathDisplay}:{pline}");
            }
        }
        return findings;
    }

    private static async Task<(List<Finding> findings, bool referenced)> AnalyzePropertyAsync(
        IPropertySymbol prop,
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        bool includeInternal,
        bool includePublic,
        bool excludeGenerated,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool referenced = false;
        if (prop.IsImplicitlyDeclared) return (findings, referenced);
        var acc = prop.DeclaredAccessibility;
        if (acc != Accessibility.Private && !includeInternal && !includePublic) return (findings, referenced);
        if (prop.IsOverride || prop.ExplicitInterfaceImplementations.Any()) return (findings, referenced);
        var defLocProp = GetSourceLocation(prop);

        // Skip if property declaration is in an excluded folder
        if (defLocProp != null && SourceTreeIsExcluded(defLocProp.SourceTree)) return (findings, referenced);

        if (excludeGenerated && defLocProp != null && IsGenerated(defLocProp.SourceTree)) return (findings, referenced);
        var defLocs = prop.Locations.Where(l => l.IsInSource).ToList();
        var allLocations = await _cache.GetSymbolLocationsAsync(prop, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        int refCount = allLocations.Count(l => !defLocs.Any(d =>
            d.SourceTree == l.SourceTree &&
            d.SourceSpan.Equals(l.SourceSpan)));

        // Fallback: manual semantic search
        if (refCount == 0)
        {
            if (await ManualSemanticSearchForAllSymbolsAsync(prop, solution, solutionProjectIds))
                refCount = 1;
        }

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocProp != null ? GetLinePosition(defLocProp) : (-1, -1);
            Document? doc = defLocProp != null ? solution.GetDocument(defLocProp.SourceTree) : null;
            string projectDisplay = BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = GetDisplayPathForDocument(doc, defLocProp?.SourceTree, project, solution);
            string displayName = GetDisplayNameForDocument(doc, defLocProp?.SourceTree);
            string fullPath = GetFullPathForDocument(doc, defLocProp?.SourceTree) ?? "(generated)";
            string projectFilePath = GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = GetIconForSymbolKind("Property");

            if (DiagnosticMode)
            {
                progress?.Report($"[Diagnostic] Property finding: declaredProject={declaredProject}, fallbackProject={fallbackProject}, declarationFileDisplay={filePathDisplay}, fullPath={fullPath}, projectFile={projectFilePath}");
                if (doc == null)
                    progress?.Report($"[Diagnostic] declaration document for property not found; falling back to project.Name '{fallbackProject}'");
            }

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
                Icon = icon
            });
            progress?.Report($"    Unused property: {type.ToDisplayString()}.{prop.Name} [{prop.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }
        return (findings, referenced);
    }

    private static async Task<(List<Finding> findings, bool referenced)> AnalyzeFieldAsync(
        IFieldSymbol field,
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        bool includeInternal,
        bool includePublic,
        bool excludeGenerated,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool referenced = false;
        if (field.IsImplicitlyDeclared) return (findings, referenced);
        var acc = field.DeclaredAccessibility;
        if (acc != Accessibility.Private && !includeInternal && !includePublic) return (findings, referenced);
        var defLocField = GetSourceLocation(field);

        // Skip if field declaration is in an excluded folder
        if (defLocField != null && SourceTreeIsExcluded(defLocField.SourceTree)) return (findings, referenced);

        if (excludeGenerated && defLocField != null && IsGenerated(defLocField.SourceTree)) return (findings, referenced);
        var defLocs = field.Locations.Where(l => l.IsInSource).ToList();
        var allLocations = await _cache.GetSymbolLocationsAsync(field, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        int refCount = allLocations.Count(l => !defLocs.Any(d =>
            d.SourceTree == l.SourceTree &&
            d.SourceSpan.Equals(l.SourceSpan)));

        // Fallback: manual semantic search
        if (refCount == 0)
        {
            if (await ManualSemanticSearchForAllSymbolsAsync(field, solution, solutionProjectIds))
                refCount = 1;
        }

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocField != null ? GetLinePosition(defLocField) : (-1, -1);
            Document? doc = defLocField != null ? solution.GetDocument(defLocField.SourceTree) : null;
            string projectDisplay = BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = GetDisplayPathForDocument(doc, defLocField?.SourceTree, project, solution);
            string displayName = GetDisplayNameForDocument(doc, defLocField?.SourceTree);
            string fullPath = GetFullPathForDocument(doc, defLocField?.SourceTree) ?? "(generated)";
            string projectFilePath = GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = GetIconForSymbolKind("Field");

            if (DiagnosticMode)
            {
                progress?.Report($"[Diagnostic] Field finding: declaredProject={declaredProject}, fallbackProject={fallbackProject}, declarationFileDisplay={filePathDisplay}, fullPath={fullPath}, projectFile={projectFilePath}");
                if (doc == null)
                    progress?.Report($"[Diagnostic] declaration document for field not found; falling back to project.Name '{fallbackProject}'");
            }

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
                Icon = icon
            });
            progress?.Report($"    Unused field: {type.ToDisplayString()}.{field.Name} [{field.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }
        return (findings, referenced);
    }

    private static async Task<List<Finding>> AnalyzeTypeUsageAsync(
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
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
            isRecord;
        if (!considerType) return findings;

        // Consider only declaration locations that are not in excluded path folders
        var defTypeLocs = type.Locations.Where(l => l.IsInSource && !SourceTreeIsExcluded(l.SourceTree)).ToList();
        if (defTypeLocs.Count == 0) return findings;

        // For interfaces, additionally check for implementations in the solution
        bool foundUsage = false;
        if (type.TypeKind == TypeKind.Interface)
        {
            foundUsage = await CheckInterfaceImplementationsAsync(type, solution, isReferenceInSolutionSource, solutionProjectIds);
            if (foundUsage) return findings;
        }

        // For classes, check derived classes (subclasses) inside the solution
        if (!foundUsage && type.TypeKind == TypeKind.Class)
        {
            foundUsage = await CheckDerivedClassesAsync(type, solution, isReferenceInSolutionSource, solutionProjectIds);
            if (foundUsage) return findings;
        }

        // General type references (variable declarations, cast, typeof, generics, attributes, etc.)
        var allTypeLocations = await _cache.GetSymbolLocationsAsync(type, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        int typeRefCount = allTypeLocations.Count(l => !defTypeLocs.Any(d =>
            d.SourceTree == l.SourceTree &&
            d.SourceSpan.Equals(l.SourceSpan)));

        // Fallback: do a manual semantic scan if SymbolFinder didn't find any references
        if (typeRefCount == 0)
        {
            var manualFound = await ManualSemanticSearchForTypeAsync(type, solution, solutionProjectIds);
            if (manualFound) typeRefCount = 1;
        }

        if (typeRefCount == 0)
        {
            var loc = defTypeLocs.FirstOrDefault();
            var (line, _) = loc != null ? GetLinePosition(loc) : (-1, -1);
            var kind = isRecord ? "Record" : type.TypeKind.ToString();

            // Try to determine the project name from the declaration document if possible
            Document? doc = loc?.SourceTree != null ? solution.GetDocument(loc.SourceTree) : null;
            string projectDisplay = BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = GetDisplayPathForDocument(doc, loc?.SourceTree, project, solution);
            string displayName = GetDisplayNameForDocument(doc, loc?.SourceTree);
            string fullPath = GetFullPathForDocument(doc, loc?.SourceTree) ?? "(generated)";
            string projectFilePath = GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = GetIconForSymbolKind("Type");

            if (DiagnosticMode)
            {
                progress?.Report($"[Diagnostic] Type finding: declaredProject={declaredProject}, fallbackProject={fallbackProject}, declarationFileDisplay={filePathDisplay}, fullPath={fullPath}, projectFile={projectFilePath}");
                if (doc == null)
                    progress?.Report($"[Diagnostic] declaration document for type not found; falling back to project.Name '{fallbackProject}'");
            }

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
                Icon = icon
            });
            progress?.Report($"    Unused type: {type.ToDisplayString()} (Kind={kind}) [{type.DeclaredAccessibility}] at {filePathDisplay}:{line}");
        }

        return findings;
    }

    private static async Task<bool> CheckInterfaceImplementationsAsync(
        INamedTypeSymbol type,
        Solution solution,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        HashSet<ProjectId> solutionProjectIds)
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
                    var ntDefLocs = nt.Locations.Where(l => l.IsInSource && !SourceTreeIsExcluded(l.SourceTree));
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

    private static Type[] GetSyntaxPatternsForSymbol(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Method => new[] { typeof(InvocationExpressionSyntax), typeof(IdentifierNameSyntax) },
            SymbolKind.Property => new[] { typeof(MemberAccessExpressionSyntax), typeof(IdentifierNameSyntax) },
            SymbolKind.Field => new[] { typeof(MemberAccessExpressionSyntax), typeof(IdentifierNameSyntax) },
            SymbolKind.NamedType => new[] { typeof(IdentifierNameSyntax), typeof(GenericNameSyntax) },
            _ => new[] { typeof(IdentifierNameSyntax) }
        };
    }

    private static async Task<bool> IsSymbolUsedInNode(ISymbol symbol, SyntaxNode node, SemanticModel model)
    {
        try
        {
            var symbolInfo = model.GetSymbolInfo(node).Symbol;
            ISymbol? foundSymbol = symbolInfo;
            if (foundSymbol == null)
            {
                var tinfo = model.GetTypeInfo(node).Type;
                foundSymbol = tinfo;
            }
            if (foundSymbol == null) return false;
            // Compare original definitions
            var symToCompare = (foundSymbol is IMethodSymbol ms && ms.ReducedFrom != null) ? ms.ReducedFrom : foundSymbol;
            var target = symbol.OriginalDefinition ?? symbol;
            if (SymbolEqualityComparer.Default.Equals(symToCompare.OriginalDefinition ?? symToCompare, target))
            {
                // Ensure not the symbol's own declaration
                var defLocs = symbol.Locations.Where(l => l.IsInSource).ToList();
                bool isDef = defLocs.Any(d => d.SourceTree == node.SyntaxTree && d.SourceSpan.Equals(node.Span));
                if (!isDef)
                {
                    return true;
                }
            }
            // Check for reflection usage
            if (IsReflectionUsagePattern(symbol, node, model))
            {
                return true;
            }
        }
        catch
        {
            // Ignore semantic exceptions
        }
        return false;
    }

    private static async Task<bool> ManualSemanticSearchForAllSymbolsAsync(
        ISymbol symbol,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds)
    {
        var shortName = symbol.Name;
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (DocumentIsExcluded(document)) continue;
                if (!document.SupportsSyntaxTree) continue;
                var root = await document.GetSyntaxRootAsync();
                if (root == null) continue;

                var text = root.GetText().ToString();
                if (!text.Contains(shortName)) continue;

                var model = await document.GetSemanticModelAsync();
                if (model == null) continue;

                // Search for various syntax patterns
                var patterns = GetSyntaxPatternsForSymbol(symbol.Kind);
                foreach (var pattern in patterns)
                {
                    var nodes = root.DescendantNodes().Where(n => pattern.IsAssignableFrom(n.GetType()));
                    foreach (var node in nodes)
                    {
                        if (await IsSymbolUsedInNode(symbol, node, model))
                            return true;
                    }
                }
            }
        }
        return false;
    }

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

    private static async Task<List<INamedTypeSymbol>> GetDeclaredTypesInProjectAsync(Project project)
    {
        var set = new List<INamedTypeSymbol>();
        foreach (var document in project.Documents)
        {
            // Skip documents that are in excluded folders (NuGet cache, bin/obj/debug)
            if (DocumentIsExcluded(document)) continue;

            if (!document.SupportsSyntaxTree) continue;
            var root = await document.GetSyntaxRootAsync();
            if (root == null) continue;
            var model = await document.GetSemanticModelAsync();
            if (model == null) continue;
            // Collect class/struct/interface/enum/record declarations
            var typeNodes = root.DescendantNodes().Where(n =>
                n is ClassDeclarationSyntax ||
                n is StructDeclarationSyntax ||
                n is InterfaceDeclarationSyntax ||
                n is EnumDeclarationSyntax ||
                n is RecordDeclarationSyntax);
            foreach (var node in typeNodes)
            {
                try
                {
                    if (model.GetDeclaredSymbol(node) is not INamedTypeSymbol symbol) continue;
                    if (symbol.IsImplicitlyDeclared) continue;
                    // avoid duplicates
                    if (!set.Any(s => SymbolEqualityComparer.Default.Equals(s, symbol)))
                        set.Add(symbol);
                }
                catch
                {
                    // ignore semantic errors in individual documents
                }
            }
        }
        return set;
    }

    private static Location? GetSourceLocation(ISymbol symbol)
        => symbol.Locations.FirstOrDefault(l => l.IsInSource);

    private static (int line, int col) GetLinePosition(Location loc)
    {
        if (loc == null || loc.SourceTree == null) return (-1, -1);
        var pos = loc.GetLineSpan().StartLinePosition;
        return (pos.Line + 1, pos.Character + 1);
    }

    private static bool IsGenerated(SyntaxTree? tree)
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

    private static bool IsNamespaceAllowed(INamespaceSymbol nsSymbol, HashSet<string> declaredNamespaces)
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

    private static bool AreNamespacesRelated(string ns1, string ns2)
    {
        // Simple check: if one is prefix of the other
        return ns1.StartsWith(ns2 + ".", StringComparison.Ordinal) ||
               ns2.StartsWith(ns1 + ".", StringComparison.Ordinal);
    }

    private static async Task<Solution?> LoadSolutionFromPath(string targetPath, MSBuildWorkspace workspace)
    {
        if (targetPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return await workspace.OpenSolutionAsync(targetPath);
        }
        else if (targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(targetPath);
            return project.Solution;
        }
        else
        {
            // try to find a .slnx/.sln in current dir, otherwise find csproj inside folder
            var slnCandidate = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.slnx").FirstOrDefault()
                               ?? Directory.GetFiles(Directory.GetCurrentDirectory(), "*.sln").FirstOrDefault();
            if (slnCandidate is null)
            {
                var csprojs = Directory.GetFiles(targetPath, "*.csproj", SearchOption.AllDirectories);
                if (csprojs.Length == 0) return null;
                var firstProject = await workspace.OpenProjectAsync(csprojs.First());
                return firstProject.Solution;
            }
            else
            {
                return await workspace.OpenSolutionAsync(slnCandidate);
            }
        }
    }

    /// <summary>
    /// Scans all source documents in the solution and returns the set of declared namespace names.
    /// - Adds empty string if there are top-level types in the global namespace.
    /// - Adds each NamespaceDeclaration and FileScopedNamespace declaration name.
    /// Documents located in excluded paths (NuGet packages, bin, obj, debug) are ignored.
    /// </summary>
    private static async Task<HashSet<string>> GetDeclaredNamespacesFromSolutionAsync(Solution solution)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                // Skip documents that are in excluded folders
                if (DocumentIsExcluded(document)) continue;

                if (!document.SupportsSyntaxTree) continue;
                var root = await document.GetSyntaxRootAsync();
                if (root == null) continue;
                // File-scoped namespaces
                var fileScoped = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>()
                    .Select(n => n.Name.ToString().Trim())
                    .Where(s => !string.IsNullOrEmpty(s));
                foreach (var ns in fileScoped) set.Add(ns);
                // Normal namespace declarations
                var nsDecls = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>()
                    .Select(n => n.Name.ToString().Trim())
                    .Where(s => !string.IsNullOrEmpty(s));
                foreach (var ns in nsDecls) set.Add(ns);
                // If there are top-level type declarations that are not inside a namespace,
                // consider the global namespace present
                var topLevelTypes = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                    .Where(t => t.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault() == null
                                && t.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault() == null);
                if (topLevelTypes.Any())
                {
                    set.Add(string.Empty); // represent global namespace with empty string
                }
            }
        }
        return set;
    }

    /// <summary>
    /// Manual semantic search fallback for type usages. Scans documents that contain the simple name and uses the semantic model.
    /// Documents located in excluded paths (NuGet packages, bin, obj, debug) are skipped.
    /// </summary>
    private static async Task<bool> ManualSemanticSearchForTypeAsync(INamedTypeSymbol typeSymbol, Solution solution, HashSet<ProjectId> solutionProjectIds)
    {
        var shortName = typeSymbol.Name;
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (DocumentIsExcluded(document)) continue;
                if (!document.SupportsSyntaxTree) continue;
                var root = await document.GetSyntaxRootAsync();
                if (root == null) continue;
                // quick textual filter
                var text = root.GetText().ToString();
                if (!text.Contains(shortName)) continue;
                var nameNodes = root.DescendantNodes().OfType<SimpleNameSyntax>()
                                     .Where(n => n.Identifier.ValueText == shortName);
                if (!nameNodes.Any()) continue;
                var model = await document.GetSemanticModelAsync();
                if (model == null) continue;
                foreach (var node in nameNodes)
                {
                    try
                    {
                        var symbolInfo = model.GetSymbolInfo(node).Symbol;
                        ISymbol? symbol = symbolInfo;
                        if (symbol == null)
                        {
                            var tinfo = model.GetTypeInfo(node).Type;
                            symbol = tinfo;
                        }
                        if (symbol == null) continue;
                        // Compare original definitions to handle constructed generics, etc.
                        var symToCompare = (symbol is IMethodSymbol ms && ms.ReducedFrom != null) ? ms.ReducedFrom : symbol;
                        var target = typeSymbol.OriginalDefinition ?? typeSymbol;
                        if (SymbolEqualityComparer.Default.Equals(symToCompare.OriginalDefinition ?? symToCompare, target))
                        {
                            // ensure this is not the type's own declaration (definition)
                            var defLocs = typeSymbol.Locations.Where(l => l.IsInSource).ToList();
                            bool isDef = defLocs.Any(d => d.SourceTree == node.SyntaxTree && d.SourceSpan.Equals(node.Span));
                            if (!isDef)
                            {
                                // ensure the document belongs to solution projects (filter out metadata-as-source)
                                var doc = solution.GetDocument(node.SyntaxTree);
                                if (doc != null && solutionProjectIds.Contains(doc.Project.Id))
                                    return true;
                            }
                        }
                        // Check for reflection usage patterns
                        if (IsReflectionUsagePattern(typeSymbol, node, model))
                        {
                            var doc = solution.GetDocument(node.SyntaxTree);
                            if (doc != null && solutionProjectIds.Contains(doc.Project.Id))
                                return true;
                        }
                    }
                    catch
                    {
                        // ignore semantic exceptions
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a method is in an API controller class
    /// </summary>
    private static bool IsApiControllerMethod(INamedTypeSymbol containingType)
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
    private static bool IsTestMethod(IMethodSymbol method, INamedTypeSymbol containingType)
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
    private static bool IsEntryPointMethod(IMethodSymbol method, INamedTypeSymbol containingType)
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
        if (IsEventHandlerMethod(method, containingType)) return true;

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
    private static bool IsEventHandlerMethod(IMethodSymbol method, INamedTypeSymbol containingType)
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


    /// <summary>
    /// Check if a symbol is used in reflection patterns
    /// </summary>
    private static bool IsReflectionUsagePattern(ISymbol symbol, SyntaxNode node, SemanticModel model)
    {
        // Check for GetMethod, GetProperty, GetField calls
        if (node is InvocationExpressionSyntax invocation)
        {
            var methodName = invocation.Expression.ToString();
            if (methodName.Contains("GetMethod") ||
                methodName.Contains("GetProperty") ||
                methodName.Contains("GetField"))
            {
                // Check if the symbol being searched for matches our target
                var symbolName = symbol.Name;
                return invocation.ArgumentList.Arguments.Any(arg =>
                    arg.Expression.ToString().Contains($"\"{symbolName}\"") ||
                    arg.Expression.ToString().Contains($"'{symbolName}'"));
            }
        }

        // Check for Activator.CreateInstance usage
        if (node is ObjectCreationExpressionSyntax creation &&
            creation.Type.ToString().Contains(symbol.ContainingType?.Name ?? ""))
        {
            return true;
        }

        // Check for typeof() expressions
        if (node is TypeOfExpressionSyntax typeOf &&
            typeOf.Type.ToString() == symbol.ContainingType?.Name)
        {
            return true;
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
    /// Main entry point for the FindUnused analyzer
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: FindUnused <targetPath>");
                Environment.Exit(1);
                return;
            }

            string targetPath = args[0];
            try
            {
                var result = await RunAnalysisAsync(targetPath, new AnalyzerConfiguration());
                if (!result.Success)
                {
                    Console.Error.WriteLine($"Analysis failed: {result.ErrorMessage}");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}
