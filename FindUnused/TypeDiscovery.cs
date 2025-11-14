namespace FindUnused;

/// <summary>
/// Handles discovery and collection of types and namespaces from projects
/// </summary>
public static class TypeDiscovery
{
    /// <summary>
    /// Builds a map of declared types for each project in the solution
    /// </summary>
    public static async Task<Dictionary<Project, List<INamedTypeSymbol>>> BuildProjectDeclaredTypesMapAsync(
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

    /// <summary>
    /// Gets all declared types in a project, including classes, structs, interfaces, enums, records, and delegates
    /// </summary>
    public static async Task<List<INamedTypeSymbol>> GetDeclaredTypesInProjectAsync(Project project)
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
            // Collect class/struct/interface/enum/record/delegate declarations
            var typeNodes = root.DescendantNodes().Where(n =>
                n is ClassDeclarationSyntax ||
                n is StructDeclarationSyntax ||
                n is InterfaceDeclarationSyntax ||
                n is EnumDeclarationSyntax ||
                n is RecordDeclarationSyntax ||
                n is DelegateDeclarationSyntax);
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

    /// <summary>
    /// Scans all source documents in the solution and returns the set of declared namespace names.
    /// - Adds empty string if there are top-level types in the global namespace.
    /// - Adds each NamespaceDeclaration and FileScopedNamespace declaration name.
    /// Documents located in excluded paths (NuGet packages, bin, obj, debug) are ignored.
    /// </summary>
    public static async Task<HashSet<string>> GetDeclaredNamespacesFromSolutionAsync(Solution solution)
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
    /// Check if a document is excluded based on its file path
    /// </summary>
    private static bool DocumentIsExcluded(Document? doc)
    {
        if (doc == null) return false;
        return Utilities.IsPathExcluded(doc.FilePath);
    }
}