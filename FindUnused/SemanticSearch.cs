namespace FindUnused;

/// <summary>
/// Handles semantic search operations for finding symbol usage
/// </summary>
public static class SemanticSearch
{
    /// <summary>
    /// Checks if a symbol is used in a specific syntax node
    /// </summary>
    public static async Task<bool> IsSymbolUsedInNode(ISymbol symbol, SyntaxNode node, SemanticModel model)
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
            // Resolve aliases
            if (foundSymbol is IAliasSymbol alias) foundSymbol = alias.Target;
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


    /// <summary>
    /// Performs manual semantic search for symbol usage across solution projects
    /// </summary>
    public static async Task<bool> ManualSemanticSearchAsync(
        ISymbol symbol,
        Solution solution,
        HashSet<ProjectId> solutionProjectIds)
    {
        if (symbol is INamedTypeSymbol typeSymbol)
        {
            var shortName = typeSymbol.Name;
            foreach (var project in solution.Projects.Where(p => solutionProjectIds.Contains(p.Id)))
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
                            ISymbol? foundSymbol = symbolInfo;
                            if (foundSymbol == null)
                            {
                                var tinfo = model.GetTypeInfo(node).Type;
                                foundSymbol = tinfo;
                            }
                            if (foundSymbol == null) continue;
                            // Resolve aliases
                            if (foundSymbol is IAliasSymbol alias) foundSymbol = alias.Target;
                            // Compare original definitions
                            var symToCompare = (foundSymbol is IMethodSymbol ms && ms.ReducedFrom != null) ? ms.ReducedFrom : foundSymbol;
                            var target = typeSymbol.OriginalDefinition ?? typeSymbol;
                            if (SymbolEqualityComparer.Default.Equals(symToCompare.OriginalDefinition ?? symToCompare, target))
                            {
                                // ensure this is not the type's own declaration (definition)
                                var defLocs = typeSymbol.Locations.Where(l => l.IsInSource).ToList();
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
                            // ignore semantic exceptions
                        }
                    }
                }
            }
            return false;
        }
        else
        {
            var shortName = symbol.Name;
            foreach (var project in solution.Projects.Where(p => solutionProjectIds.Contains(p.Id)))
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
    }

    /// <summary>
    /// Gets syntax patterns to search for based on symbol kind
    /// </summary>
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

    /// <summary>
    /// Check if a symbol is used in reflection patterns
    /// </summary>
    private static bool IsReflectionUsagePattern(ISymbol symbol, SyntaxNode node, SemanticModel model)
    {
        try
        {
            // Check for GetMethod, GetProperty, GetField calls on System.Type
            if (node is InvocationExpressionSyntax invocation)
            {
                var methodSymbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (methodSymbol != null &&
                    (methodSymbol.Name == "GetMethod" || methodSymbol.Name == "GetProperty" || methodSymbol.Name == "GetField") &&
                    methodSymbol.ContainingType?.Name == "Type" &&
                    invocation.ArgumentList.Arguments.Count > 0)
                {
                    // Check if the first argument is a string literal matching the symbol name
                    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                    if (firstArg is LiteralExpressionSyntax literal &&
                        literal.Kind() == SyntaxKind.StringLiteralExpression &&
                        literal.Token.ValueText == symbol.Name)
                    {
                        return true;
                    }
                }

                // Check for Activator.CreateInstance calls
                if (methodSymbol != null &&
                    methodSymbol.Name == "CreateInstance" &&
                    methodSymbol.ContainingType?.Name == "Activator")
                {
                    // Check if any type argument matches the symbol's containing type
                    foreach (var arg in invocation.ArgumentList.Arguments)
                    {
                        var typeInfo = model.GetTypeInfo(arg.Expression).Type;
                        if (typeInfo != null && SymbolEqualityComparer.Default.Equals(typeInfo, symbol.ContainingType))
                        {
                            return true;
                        }
                    }
                }
            }

            // Check for typeof() expressions
            if (node is TypeOfExpressionSyntax typeOf)
            {
                var typeInfo = model.GetTypeInfo(typeOf.Type).Type;
                if (typeInfo != null && SymbolEqualityComparer.Default.Equals(typeInfo, symbol.ContainingType))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore semantic analysis errors
        }

        return false;
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