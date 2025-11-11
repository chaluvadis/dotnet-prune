namespace FindUnused;
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
    public string Project { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public string SymbolKind { get; set; } = string.Empty;
    public string ContainingType { get; set; } = string.Empty;
    public string SymbolName { get; set; } = string.Empty;
    public string Accessibility { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
}
/// <summary>
/// Main analysis class for finding unused C# code symbols
/// </summary>
public class FindUnusedAnalyzer
{
    private static JsonSerializerOptions GetOptions() => new() { WriteIndented = true };
    /// <summary>
    /// Run the analysis with specified parameters
    /// </summary>
    /// <param name="targetPath">Path to .slnx, .sln, .csproj file or folder to analyze</param>
    /// <param name="includePublic">Include public symbols in analysis</param>
    /// <param name="includeInternal">Include internal symbols in analysis</param>
    /// <param name="excludeGenerated">Exclude generated code from analysis</param>
    /// <param name="reportPath">Output report file path (null for no file output)</param>
    /// <param name="progress">Optional progress reporter for UI updates</param>
    /// <returns>Analysis results</returns>
    public static async Task<AnalysisResult> RunAnalysisAsync(
        string targetPath,
        bool includePublic = true,
        bool includeInternal = true,
        bool excludeGenerated = true,
        string? reportPath = null,
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
            // Analyze each project
            foreach (var project in solution.Projects)
            {
                if (solutionProjectIds == null) continue; // safety check
                var projectFindings = await AnalyzeProjectAsync(
                    project,
                    solution,
                    solutionProjectIds,
                    projectDeclaredTypes,
                    declaredNamespaces,
                    includePublic,
                    includeInternal,
                    excludeGenerated,
                    IsReferenceInSolutionSource,
                    progress);
                findings.AddRange(projectFindings);
            }
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                // Finalize analysis
                progress?.Report($"\nAnalysis complete. Findings: {findings.Count}");
                await WriteReportAsync(findings, reportPath, progress);
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
        return (solution, solutionProjectIds);
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
                    isReferenceInSolutionSource,
                    progress);
                projectFindings.AddRange(typeFindings);
                // Only check type usage if no members were referenced
                if (!typeHasReferencedMember)
                {
                    var typeUsageFindings = await AnalyzeTypeUsageAsync(
                        type,
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
        var defTypeLocs = type.Locations.Where(l => l.IsInSource).ToList();
        if (defTypeLocs.Count == 0) return (findings, typeHasReferencedMember); // nothing in source to analyze
        // Analyze members first and record member-level usage
        foreach (var member in type.GetMembers())
        {
            try
            {
                if (member.IsImplicitlyDeclared) continue;
                var defLoc = GetSourceLocation(member);
                if (excludeGenerated && defLoc != null && IsGenerated(defLoc.SourceTree)) continue;
                if (member is IMethodSymbol method)
                {
                    var (methodFindings, memberReferenced) = await AnalyzeMethodAsync(
                        method, type, project, solution, includePublic, includeInternal,
                        excludeGenerated, compilation, isReferenceInSolutionSource, progress);
                    findings.AddRange(methodFindings);
                    if (memberReferenced) typeHasReferencedMember = true;
                }
                else if (member is IPropertySymbol prop)
                {
                    var (propertyFindings, memberReferenced) = await AnalyzePropertyAsync(
                        prop, type, project, solution, includeInternal, includePublic,
                        excludeGenerated, isReferenceInSolutionSource, progress);
                    findings.AddRange(propertyFindings);
                    if (memberReferenced) typeHasReferencedMember = true;
                }
                else if (member is IFieldSymbol field)
                {
                    var (fieldFindings, memberReferenced) = await AnalyzeFieldAsync(
                        field, type, project, solution, includeInternal, includePublic,
                        excludeGenerated, isReferenceInSolutionSource, progress);
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
    private static async Task<(List<Finding> findings, bool referenced)> AnalyzeMethodAsync(
        IMethodSymbol method,
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        bool includePublic,
        bool includeInternal,
        bool excludeGenerated,
        Compilation compilation,
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
        var defLoc = GetSourceLocation(method);
        if (excludeGenerated && defLoc != null && IsGenerated(defLoc.SourceTree)) return (findings, referenced);
        // Find references across the solution
        var references = await SymbolFinder.FindReferencesAsync(method, solution);
        var defLocations = method.Locations.Where(l => l.IsInSource).ToList();
        int refCount = 0;
        foreach (var rr in references)
        {
            foreach (var loc in rr.Locations)
            {
                if (!isReferenceInSolutionSource(loc.Location, solution, new HashSet<ProjectId>())) continue;
                bool isDefinitionLocation = defLocations.Any(d =>
                    d.SourceTree == loc.Location.SourceTree &&
                    d.SourceSpan.Equals(loc.Location.SourceSpan));
                if (!isDefinitionLocation) refCount++;
            }
        }
        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLoc != null ? GetLinePosition(defLoc) : (-1, -1);
            findings.Add(new Finding
            {
                Project = project.Name,
                FilePath = defLoc?.SourceTree?.FilePath ?? "(generated)",
                Line = line,
                SymbolKind = "Method",
                ContainingType = type.ToDisplayString(),
                SymbolName = method.ToDisplayString(),
                Accessibility = method.DeclaredAccessibility.ToString(),
                Remarks = "No references found in solution source"
            });
            progress?.Report($"    Unused method: {type.ToDisplayString()}.{method.Name} [{method.DeclaredAccessibility}] at {defLoc?.SourceTree?.FilePath}:{line}");
        }
        // Analyze method parameters
        var parameterFindings = await AnalyzeMethodParametersAsync(method, type, project, solution, isReferenceInSolutionSource, progress);
        findings.AddRange(parameterFindings);
        return (findings, referenced);
    }
    private static async Task<List<Finding>> AnalyzeMethodParametersAsync(
        IMethodSymbol method,
        INamedTypeSymbol type,
        Project project,
        Solution solution,
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        foreach (var param in method.Parameters)
        {
            if (param.RefKind != RefKind.None) continue;
            var paramRefs = await SymbolFinder.FindReferencesAsync(param, solution);
            var paramDefLocs = param.Locations.Where(l => l.IsInSource).ToList();
            int paramRefCount = 0;
            foreach (var rr in paramRefs)
            {
                foreach (var loc in rr.Locations)
                {
                    if (!isReferenceInSolutionSource(loc.Location, solution, new HashSet<ProjectId>())) continue;
                    bool isDefLoc = paramDefLocs.Any(d => d.SourceTree == loc.Location.SourceTree && d.SourceSpan.Equals(loc.Location.SourceSpan));
                    if (!isDefLoc) paramRefCount++;
                }
            }
            if (paramRefCount == 0)
            {
                var pLoc = paramDefLocs.FirstOrDefault();
                var (pline, _) = pLoc != null ? GetLinePosition(pLoc) : (-1, -1);
                findings.Add(new Finding
                {
                    Project = project.Name,
                    FilePath = pLoc?.SourceTree?.FilePath ?? "(generated)",
                    Line = pline,
                    SymbolKind = "Parameter",
                    ContainingType = type.ToDisplayString(),
                    SymbolName = $"{method.ToDisplayString()} :: {param.Name}",
                    Accessibility = method.DeclaredAccessibility.ToString(),
                    Remarks = "Parameter never referenced in solution source"
                });
                progress?.Report($"      Unused parameter: {method.ToDisplayString()} :: {param.Name} at {pLoc?.SourceTree?.FilePath}:{pline}");
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
        if (excludeGenerated && defLocProp != null && IsGenerated(defLocProp.SourceTree)) return (findings, referenced);
        var refs = await SymbolFinder.FindReferencesAsync(prop, solution);
        var defLocs = prop.Locations.Where(l => l.IsInSource).ToList();
        int refCount = 0;
        foreach (var rr in refs)
        {
            foreach (var loc in rr.Locations)
            {
                if (!isReferenceInSolutionSource(loc.Location, solution, new HashSet<ProjectId>())) continue;
                bool isDefinitionLocation = defLocs.Any(d =>
                    d.SourceTree == loc.Location.SourceTree &&
                    d.SourceSpan.Equals(loc.Location.SourceSpan));
                if (!isDefinitionLocation) refCount++;
            }
        }
        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocProp != null ? GetLinePosition(defLocProp) : (-1, -1);
            findings.Add(new Finding
            {
                Project = project.Name,
                FilePath = defLocProp?.SourceTree?.FilePath ?? "(generated)",
                Line = line,
                SymbolKind = "Property",
                ContainingType = type.ToDisplayString(),
                SymbolName = prop.ToDisplayString(),
                Accessibility = prop.DeclaredAccessibility.ToString(),
                Remarks = "No references found in solution source"
            });
            progress?.Report($"    Unused property: {type.ToDisplayString()}.{prop.Name} [{prop.DeclaredAccessibility}] at {defLocProp?.SourceTree?.FilePath}:{line}");
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
        Func<Location, Solution, HashSet<ProjectId>, bool> isReferenceInSolutionSource,
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool referenced = false;
        if (field.IsImplicitlyDeclared) return (findings, referenced);
        var acc = field.DeclaredAccessibility;
        if (acc != Accessibility.Private && !includeInternal && !includePublic) return (findings, referenced);
        var defLocField = GetSourceLocation(field);
        if (excludeGenerated && defLocField != null && IsGenerated(defLocField.SourceTree)) return (findings, referenced);
        var refs = await SymbolFinder.FindReferencesAsync(field, solution);
        var defLocs = field.Locations.Where(l => l.IsInSource).ToList();
        int refCount = 0;
        foreach (var rr in refs)
        {
            foreach (var loc in rr.Locations)
            {
                if (!isReferenceInSolutionSource(loc.Location, solution, new HashSet<ProjectId>())) continue;
                bool isDefinitionLocation = defLocs.Any(d =>
                    d.SourceTree == loc.Location.SourceTree &&
                    d.SourceSpan.Equals(loc.Location.SourceSpan));
                if (!isDefinitionLocation) refCount++;
            }
        }
        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocField != null ? GetLinePosition(defLocField) : (-1, -1);
            findings.Add(new Finding
            {
                Project = project.Name,
                FilePath = defLocField?.SourceTree?.FilePath ?? "(generated)",
                Line = line,
                SymbolKind = "Field",
                ContainingType = type.ToDisplayString(),
                SymbolName = field.ToDisplayString(),
                Accessibility = field.DeclaredAccessibility.ToString(),
                Remarks = "No references found in solution source"
            });
            progress?.Report($"    Unused field: {type.ToDisplayString()}.{field.Name} [{field.DeclaredAccessibility}] at {defLocField?.SourceTree?.FilePath}:{line}");
        }
        return (findings, referenced);
    }
    private static async Task<List<Finding>> AnalyzeTypeUsageAsync(
        INamedTypeSymbol type,
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
        var defTypeLocs = type.Locations.Where(l => l.IsInSource).ToList();
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
        var typeReferences = await SymbolFinder.FindReferencesAsync(type, solution);
        int typeRefCount = 0;
        foreach (var rr in typeReferences)
        {
            foreach (var loc in rr.Locations)
            {
                if (!isReferenceInSolutionSource(loc.Location, solution, solutionProjectIds)) continue;
                // Exclude the type's own declaration locations
                bool isDefinitionLocation = defTypeLocs.Any(d =>
                    d.SourceTree == loc.Location.SourceTree &&
                    d.SourceSpan.Equals(loc.Location.SourceSpan));
                if (!isDefinitionLocation) typeRefCount++;
            }
        }
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
            findings.Add(new Finding
            {
                Project = type.ContainingNamespace?.Name ?? "Unknown",
                FilePath = loc?.SourceTree?.FilePath ?? "(generated)",
                Line = line,
                SymbolKind = "Type",
                ContainingType = type.ContainingType?.ToDisplayString() ?? "",
                SymbolName = type.ToDisplayString(),
                Accessibility = type.DeclaredAccessibility.ToString(),
                Remarks = $"No references found in solution source (TypeKind={kind})"
            });
            progress?.Report($"    Unused type: {type.ToDisplayString()} (Kind={kind}) [{type.DeclaredAccessibility}] at {loc?.SourceTree?.FilePath}:{line}");
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
                    var ntDefLocs = nt.Locations.Where(l => l.IsInSource);
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
    private static async Task WriteReportAsync(List<Finding> findings, string? reportPath, IProgress<string>? progress)
    {
        if (!string.IsNullOrEmpty(reportPath))
        {
            try
            {
                var json = JsonSerializer.Serialize(findings, GetOptions());
                await File.WriteAllTextAsync(reportPath, json);
                progress?.Report($"Report written to {reportPath}");
            }
            catch (Exception ex)
            {
                progress?.Report($"Failed to write report: {ex.Message}");
            }
        }
        else
        {
            progress?.Report("\nFindings (sample):");
            foreach (var f in findings.Take(10))
            {
                progress?.Report($"{f.Project} | {f.SymbolKind} | {f.ContainingType} | {f.SymbolName} | {f.Accessibility} | {f.FilePath}:{f.Line} => {f.Remarks}");
            }
            progress?.Report("Done.");
        }
    }
    private static async Task<List<INamedTypeSymbol>> GetDeclaredTypesInProjectAsync(Project project)
    {
        var set = new List<INamedTypeSymbol>();
        foreach (var document in project.Documents)
        {
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
        var first = text.Lines.Take(5).Select(l => l.ToString()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (first == null) return false;
        var markers = new[] { "<auto-generated", "generated by", "<autogenerated" };
        var low = first.ToLowerInvariant();
        return markers.Any(m => low.Contains(m));
    }
    private static bool IsNamespaceAllowed(INamespaceSymbol nsSymbol, HashSet<string> declaredNamespaces)
    {
        // If declaredNamespaces was left intentionally empty (no filtering), allow everything
        if (declaredNamespaces == null || declaredNamespaces.Count == 0)
            return true;
        // If the type is in the global namespace, allow only if solution declared global types (empty string)
        if (nsSymbol == null || nsSymbol.IsGlobalNamespace)
            return declaredNamespaces.Contains(string.Empty);
        var ns = nsSymbol.ToDisplayString();
        if (string.IsNullOrEmpty(ns)) return declaredNamespaces.Contains(string.Empty);
        // Allow if any declared namespace equals the namespace or is a parent (prefix match)
        foreach (var declared in declaredNamespaces)
        {
            if (string.IsNullOrEmpty(declared))
            {
                // declared global namespace doesn't match named namespaces
                continue;
            }
            if (ns.Equals(declared, StringComparison.Ordinal) ||
                ns.StartsWith(declared + ".", StringComparison.Ordinal))
                return true;
        }
        return false;
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
    /// </summary>
    private static async Task<HashSet<string>> GetDeclaredNamespacesFromSolutionAsync(Solution solution)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
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
    /// </summary>
    private static async Task<bool> ManualSemanticSearchForTypeAsync(INamedTypeSymbol typeSymbol, Solution solution, HashSet<ProjectId> solutionProjectIds)
    {
        var shortName = typeSymbol.Name;
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
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
}