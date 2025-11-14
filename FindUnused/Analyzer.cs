namespace FindUnused;

/// <summary>
/// Core analysis logic for finding unused symbols
/// </summary>
public static class Analyzer
{
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
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        bool typeHasReferencedMember = false;
        // Skip types outside the solution-declared namespaces
        if (!Utilities.IsNamespaceAllowed(type.ContainingNamespace, declaredNamespaces))
            return (findings, typeHasReferencedMember);
        if (type.IsImplicitlyDeclared) return (findings, typeHasReferencedMember);
        // Respect visibility options for types
        var tAcc = type.DeclaredAccessibility;
        if (tAcc == Accessibility.Public && !includePublic) return (findings, typeHasReferencedMember);
        if (tAcc == Accessibility.Internal && !includeInternal && tAcc != Accessibility.Private) return (findings, typeHasReferencedMember);
        if (tAcc == Accessibility.Protected || tAcc == Accessibility.ProtectedOrInternal) return (findings, typeHasReferencedMember);

        // Consider only declaration locations that are not excluded (bin/obj/nuget/packages/debug)
        var defTypeLocs = type.Locations.Where(l => l.IsInSource && !Utilities.SourceTreeIsExcluded(l.SourceTree)).ToList();
        if (defTypeLocs.Count == 0) return (findings, typeHasReferencedMember); // nothing in source to analyze (or all declarations excluded)

        // Analyze members first and record member-level usage
        foreach (var member in type.GetMembers())
        {
            try
            {
                if (member.IsImplicitlyDeclared) continue;
                var defLoc = Utilities.GetSourceLocation(member);

                // Skip if the member's source file is in an excluded path
                if (defLoc != null && Utilities.SourceTreeIsExcluded(defLoc.SourceTree)) continue;

                if (excludeGenerated && defLoc != null && Utilities.IsGenerated(defLoc.SourceTree)) continue;

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
        if (acc == Accessibility.Internal && !includeInternal && acc != Accessibility.Private) return (findings, referenced);
        if (acc == Accessibility.Protected || acc == Accessibility.ProtectedOrInternal) return (findings, referenced);
        var entry = compilation.GetEntryPoint(CancellationToken.None);
        if (entry != null && SymbolEqualityComparer.Default.Equals(entry, method)) return (findings, referenced);
        // Skip entry point methods as they are not considered unused
        if (Utilities.IsEntryPointMethod(method, type))
        {
            return (findings, false); // Return referenced=true to indicate it's an entry point
        }
        var defLoc = Utilities.GetSourceLocation(method);

        // Skip if definition is in an excluded folder
        if (defLoc != null && Utilities.SourceTreeIsExcluded(defLoc.SourceTree)) return (findings, referenced);

        if (excludeGenerated && defLoc != null && Utilities.IsGenerated(defLoc.SourceTree)) return (findings, referenced);
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
            if (await SemanticSearch.ManualSemanticSearchAsync(method, solution, solutionProjectIds))
                refCount = 1;
        }

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLoc != null ? Utilities.GetLinePosition(defLoc) : (-1, -1);
            Document? doc = defLoc != null ? solution.GetDocument(defLoc.SourceTree) : null;
            string projectDisplay = Utilities.BuildProjectDisplayNameFrom(project, doc);

            // File path displayed relative to project/solution or using virtual folder structure in Project
            string filePathDisplay = Utilities.GetDisplayPathForDocument(doc, defLoc?.SourceTree, project, solution);
            string displayName = Utilities.GetDisplayNameForDocument(doc, defLoc?.SourceTree);

            // Full path for extension-level use
            string fullPath = Utilities.GetFullPathForDocument(doc, defLoc?.SourceTree) ?? "(generated)";

            // Project file path (absolute) for extension-level use
            string projectFilePath = Utilities.GetProjectFilePath(project, doc) ?? "(unknown)";

            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = Utilities.GetIconForSymbolKind("Method");

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
        IProgress<string>? progress)
    {
        var findings = new List<Finding>();
        foreach (var param in method.Parameters)
        {
            if (param.RefKind != RefKind.None) continue;
            var paramRefs = await SymbolFinder.FindReferencesAsync(param, solution);
            var paramDefLocs = param.Locations.Where(l => l.IsInSource && !Utilities.SourceTreeIsExcluded(l.SourceTree)).ToList();
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
                var (pline, _) = pLoc != null ? Utilities.GetLinePosition(pLoc) : (-1, -1);
                Document? doc = pLoc != null ? solution.GetDocument(pLoc.SourceTree) : null;
                string projectDisplay = Utilities.BuildProjectDisplayNameFrom(project, doc);
                string filePathDisplay = Utilities.GetDisplayPathForDocument(doc, pLoc?.SourceTree, project, solution);
                string displayName = Utilities.GetDisplayNameForDocument(doc, pLoc?.SourceTree);
                string fullPath = Utilities.GetFullPathForDocument(doc, pLoc?.SourceTree) ?? "(generated)";
                string projectFilePath = Utilities.GetProjectFilePath(project, doc) ?? "(unknown)";
                string declaredProject = doc?.Project?.Name ?? "(null)";
                string fallbackProject = project?.Name ?? "(null)";
                string icon = Utilities.GetIconForSymbolKind("Parameter");

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

    /// <summary>
    /// Analyze property usage
    /// </summary>
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
        var defLocProp = Utilities.GetSourceLocation(prop);

        // Skip if property declaration is in an excluded folder
        if (defLocProp != null && Utilities.SourceTreeIsExcluded(defLocProp.SourceTree)) return (findings, referenced);

        if (excludeGenerated && defLocProp != null && Utilities.IsGenerated(defLocProp.SourceTree)) return (findings, referenced);
        var defLocs = prop.Locations.Where(l => l.IsInSource).ToList();
        var allLocations = await _cache.GetSymbolLocationsAsync(prop, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        int refCount = allLocations.Count(l => !defLocs.Any(d =>
            d.SourceTree == l.SourceTree &&
            d.SourceSpan.Equals(l.SourceSpan)));

        // Fallback: manual semantic search
        if (refCount == 0)
        {
            if (await SemanticSearch.ManualSemanticSearchAsync(prop, solution, solutionProjectIds))
                refCount = 1;
        }

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocProp != null ? Utilities.GetLinePosition(defLocProp) : (-1, -1);
            Document? doc = defLocProp != null ? solution.GetDocument(defLocProp.SourceTree) : null;
            string projectDisplay = Utilities.BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = Utilities.GetDisplayPathForDocument(doc, defLocProp?.SourceTree, project, solution);
            string displayName = Utilities.GetDisplayNameForDocument(doc, defLocProp?.SourceTree);
            string fullPath = Utilities.GetFullPathForDocument(doc, defLocProp?.SourceTree) ?? "(generated)";
            string projectFilePath = Utilities.GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = Utilities.GetIconForSymbolKind("Property");

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

    /// <summary>
    /// Analyze field usage
    /// </summary>
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
        var defLocField = Utilities.GetSourceLocation(field);

        // Skip if field declaration is in an excluded folder
        if (defLocField != null && Utilities.SourceTreeIsExcluded(defLocField.SourceTree)) return (findings, referenced);

        if (excludeGenerated && defLocField != null && Utilities.IsGenerated(defLocField.SourceTree)) return (findings, referenced);
        var defLocs = field.Locations.Where(l => l.IsInSource).ToList();
        var allLocations = await _cache.GetSymbolLocationsAsync(field, project.Id, solution, solutionProjectIds, isReferenceInSolutionSource);
        int refCount = allLocations.Count(l => !defLocs.Any(d =>
            d.SourceTree == l.SourceTree &&
            d.SourceSpan.Equals(l.SourceSpan)));

        // Fallback: manual semantic search
        if (refCount == 0)
        {
            if (await SemanticSearch.ManualSemanticSearchAsync(field, solution, solutionProjectIds))
                refCount = 1;
        }

        if (refCount > 0)
            referenced = true;
        else
        {
            var (line, _) = defLocField != null ? Utilities.GetLinePosition(defLocField) : (-1, -1);
            Document? doc = defLocField != null ? solution.GetDocument(defLocField.SourceTree) : null;
            string projectDisplay = Utilities.BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = Utilities.GetDisplayPathForDocument(doc, defLocField?.SourceTree, project, solution);
            string displayName = Utilities.GetDisplayNameForDocument(doc, defLocField?.SourceTree);
            string fullPath = Utilities.GetFullPathForDocument(doc, defLocField?.SourceTree) ?? "(generated)";
            string projectFilePath = Utilities.GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = Utilities.GetIconForSymbolKind("Field");

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

    /// <summary>
    /// Analyze type usage
    /// </summary>
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
            type.TypeKind == TypeKind.Delegate ||
            isRecord;
        if (!considerType) return findings;

        // Special handling for static classes: don't report as unused
        // Static classes are often used implicitly through their members
        if (type.IsStatic) return findings;

        // Consider only declaration locations that are not in excluded path folders
        var defTypeLocs = type.Locations.Where(l => l.IsInSource && !Utilities.SourceTreeIsExcluded(l.SourceTree)).ToList();
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
            var manualFound = await SemanticSearch.ManualSemanticSearchAsync(type, solution, solutionProjectIds);
            if (manualFound) typeRefCount = 1;
        }

        if (typeRefCount == 0)
        {
            var loc = defTypeLocs.FirstOrDefault();
            var (line, _) = loc != null ? Utilities.GetLinePosition(loc) : (-1, -1);
            var kind = isRecord ? "Record" : type.TypeKind.ToString();

            // Try to determine the project name from the declaration document if possible
            Document? doc = loc?.SourceTree != null ? solution.GetDocument(loc.SourceTree) : null;
            string projectDisplay = Utilities.BuildProjectDisplayNameFrom(project, doc);
            string filePathDisplay = Utilities.GetDisplayPathForDocument(doc, loc?.SourceTree, project, solution);
            string displayName = Utilities.GetDisplayNameForDocument(doc, loc?.SourceTree);
            string fullPath = Utilities.GetFullPathForDocument(doc, loc?.SourceTree) ?? "(generated)";
            string projectFilePath = Utilities.GetProjectFilePath(project, doc) ?? "(unknown)";
            string declaredProject = doc?.Project?.Name ?? "(null)";
            string fallbackProject = project?.Name ?? "(null)";
            string icon = Utilities.GetIconForSymbolKind("Type");

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

    /// <summary>
    /// Check for interface implementations
    /// </summary>
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
                    var ntDefLocs = nt.Locations.Where(l => l.IsInSource && !Utilities.SourceTreeIsExcluded(l.SourceTree));
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

    // Cache instance
    private static readonly AnalyzerCache _cache = new();

    // Diagnostic mode
    public static bool DiagnosticMode { get; set; } = false;
}