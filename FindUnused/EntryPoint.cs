namespace FindUnused;

/// <summary>
/// Main entry point for the FindUnused analyzer
/// </summary>
public static class EntryPoint
{
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
            var declaredNamespaces = await TypeDiscovery.GetDeclaredNamespacesFromSolutionAsync(solution);
            progress?.Report($"Declared namespaces found by syntax scan: {declaredNamespaces.Count}");
            var projectDeclaredTypes = await TypeDiscovery.BuildProjectDeclaredTypesMapAsync(solution, declaredNamespaces);
            progress?.Report($"Declared namespaces after augmentation: {declaredNamespaces.Count}");
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
    /// Find the solution file that contains the given project file
    /// </summary>
    private static string? FindContainingSolution(string projectPath)
    {
        var directory = Path.GetDirectoryName(projectPath);
        while (!string.IsNullOrEmpty(directory))
        {
            var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly)
                                    .Concat(Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly));
            foreach (var slnFile in slnFiles)
            {
                try
                {
                    // Quick check: see if the solution file contains the project file path
                    var content = File.ReadAllText(slnFile);
                    var projectName = Path.GetFileName(projectPath);
                    if (content.Contains(projectName))
                    {
                        return slnFile;
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