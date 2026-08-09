namespace FindUnused;

/// <summary>
/// Main entry point for the FindUnused analyzer
/// </summary>
public static class EntryPoint
{
    private static readonly Lazy<bool> s_msbuildRegistered = new(() =>
    {
        MSBuildLocator.RegisterDefaults();
        return true;
    });

    /// <summary>
    /// Run the analysis with specified parameters
    /// </summary>
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
            var declaredNamespaces = await TypeDiscovery.GetDeclaredNamespacesFromSolutionAsync(solution);
            progress?.Report($"Declared namespaces found by syntax scan: {declaredNamespaces.Count}");
            var projectDeclaredTypes = await TypeDiscovery.BuildProjectDeclaredTypesMapAsync(solution, declaredNamespaces);
            progress?.Report($"Declared namespaces after augmentation: {declaredNamespaces.Count}");
            
            // Create per-analysis cache (replaces unbounded static cache)
            var analyzerCache = new AnalyzerCache();
            
            // Analyze each project in parallel
            if (solutionProjectIds != null)
            {
                var projectTasks = solution.Projects.Select(project => Analyzer.AnalyzeProjectAsync(
                    project,
                    solution,
                    solutionProjectIds,
                    projectDeclaredTypes,
                    declaredNamespaces,
                    analysisConfig.IncludePublicSymbols,
                    analysisConfig.IncludeInternalSymbols,
                    analysisConfig.ExcludeGeneratedCode,
                    IsReferenceInSolutionSource,
                    analyzerCache,
                    progress));
                var projectFindingsArrays = await Task.WhenAll(projectTasks);
                var findings = new List<Finding>();
                foreach (var arr in projectFindingsArrays)
                {
                    findings.AddRange(arr);
                }
                return new AnalysisResult
                {
                    Success = true,
                    Findings = findings
                };
            }
            
            return new AnalysisResult
            {
                Success = true,
                Findings = []
            };
        }
        catch (Exception ex)
        {
            return new AnalysisResult
            {
                Success = false,
                ErrorMessage = $"Analysis failed: {ex.Message}",
                Findings = []
            };
        }
    }

    /// <summary>
    /// Check if a location reference is within the solution source
    /// </summary>
    private static bool IsReferenceInSolutionSource(Location loc, Solution solution, HashSet<ProjectId> solutionProjectIds)
    {
        if (loc == null || !loc.IsInSource) return false;
        var doc = solution.GetDocument(loc.SourceTree);
        return doc != null && solutionProjectIds.Contains(doc.Project.Id);
    }

    /// <summary>
    /// Setup workspace and load solution from target path
    /// </summary>
    private static async Task<(Solution? solution, HashSet<ProjectId>? projectIds)> SetupWorkspaceAsync(string targetPath, IProgress<string>? progress)
    {
        // Ensure MSBuild is registered only once per process
        _ = s_msbuildRegistered.Value;
        
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
    /// Load solution from various path types
    /// </summary>
    private static async Task<Solution?> LoadSolutionFromPath(string targetPath, MSBuildWorkspace workspace)
    {
        if (targetPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) || targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return await workspace.OpenSolutionAsync(targetPath);
        }
        else if (targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            // For .csproj, try to find and load the containing solution for cross-project analysis
            var solutionPath = FindContainingSolution(targetPath);
            if (solutionPath != null)
            {
                return await workspace.OpenSolutionAsync(solutionPath);
            }
            else
            {
                // Fallback to loading just the project
                var project = await workspace.OpenProjectAsync(targetPath);
                return project.Solution;
            }
        }
        else
        {
            // try to find a .slnx/.sln in the target directory, otherwise find csproj inside folder
            var slnCandidate = Directory.GetFiles(targetPath, "*.slnx", SearchOption.TopDirectoryOnly).FirstOrDefault()
                                ?? Directory.GetFiles(targetPath, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
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
    /// Find the solution file that contains the given project file.
    /// Uses streaming file inspection instead of loading entire file.
    /// </summary>
    private static string? FindContainingSolution(string projectPath)
    {
        var directory = Path.GetDirectoryName(projectPath);
        var projectName = Path.GetFileName(projectPath);
        while (!string.IsNullOrEmpty(directory))
        {
            var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly)
                                    .Concat(Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly));
            foreach (var slnFile in slnFiles)
            {
                try
                {
                    // Stream the file line by line instead of loading entire content
                    // This avoids large allocations for big solution files
                    foreach (var line in File.ReadLines(slnFile).Take(50))
                    {
                        if (line.Contains(projectName, StringComparison.OrdinalIgnoreCase))
                        {
                            return slnFile;
                        }
                    }
                }
                catch
                {
                    // Ignore read errors
                }
            }
            // Move up one directory
            directory = Path.GetDirectoryName(directory);
        }
        return null;
    }
}
