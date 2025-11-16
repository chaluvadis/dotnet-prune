namespace FindUnused;

/// <summary>
/// Handles semantic search operations for finding symbol usage
/// </summary>
public static class SemanticSearch
{
    /// <summary>
    /// Checks if a symbol is used in a specific syntax node
    /// </summary>
    public static async Task<bool> IsSymbolUsedInNode(
        ISymbol symbol,
        SyntaxNode node,
        SemanticModel model
    )
    {
        try
        {
            ISymbol? foundSymbol = model.GetSymbolInfo(node).Symbol;
            foundSymbol ??= model.GetTypeInfo(node).Type;
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
            SymbolKind.Method => [typeof(InvocationExpressionSyntax), typeof(IdentifierNameSyntax)],
            SymbolKind.Property => [typeof(MemberAccessExpressionSyntax), typeof(IdentifierNameSyntax)],
            SymbolKind.Field => [typeof(MemberAccessExpressionSyntax), typeof(IdentifierNameSyntax)],
            SymbolKind.NamedType => [typeof(IdentifierNameSyntax), typeof(GenericNameSyntax), typeof(LiteralExpressionSyntax)],
            _ => [typeof(IdentifierNameSyntax)]
        };
    }

    /// <summary>
    /// Check if a symbol is used in reflection patterns
    /// </summary>
    private static bool IsReflectionUsagePattern(ISymbol symbol, SyntaxNode node, SemanticModel model)
    {
        try
        {
            // Check for string literals used in reflection calls
            if (node is LiteralExpressionSyntax literal && literal.Kind() == SyntaxKind.StringLiteralExpression)
            {
                var value = literal.Token.ValueText;
                // Check if this literal matches the symbol name or full name
                bool nameMatches = value == symbol.Name;
                if (symbol is INamedTypeSymbol typeSymbol)
                {
                    nameMatches = nameMatches || value == typeSymbol.ToDisplayString();
                }
                if (nameMatches)
                {
                    // Check if this literal is an argument to a reflection method
                    var argument = literal.Parent as ArgumentSyntax;
                    if (argument != null)
                    {
                        var argumentList = argument.Parent as ArgumentListSyntax;
                        if (argumentList != null)
                        {
                            var inv = argumentList.Parent as InvocationExpressionSyntax;
                            if (inv != null)
                            {
                                var methodSymbol = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                                if (methodSymbol != null)
                                {
                                    // Type.GetType, Assembly.GetType
                                    if (methodSymbol.Name == "GetType" &&
                                        (methodSymbol.ContainingType?.Name == "Type" || methodSymbol.ContainingType?.Name == "Assembly"))
                                    {
                                        return true;
                                    }
                                    // Type.GetMethod, GetProperty, etc.
                                    if ((methodSymbol.Name == "GetMethod" || methodSymbol.Name == "GetProperty" ||
                                         methodSymbol.Name == "GetField" || methodSymbol.Name == "GetNestedType") &&
                                        methodSymbol.ContainingType?.Name == "Type")
                                    {
                                        return true;
                                    }
                                    // Activator.CreateInstance with string arguments
                                    if (methodSymbol.Name == "CreateInstance" && methodSymbol.ContainingType?.Name == "Activator")
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Check for GetMethod, GetProperty, GetField, GetNestedType calls on System.Type
            if (node is InvocationExpressionSyntax invocation)
            {
                var methodSymbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (methodSymbol != null &&
                    (methodSymbol.Name == "GetMethod" || methodSymbol.Name == "GetProperty" || methodSymbol.Name == "GetField" || methodSymbol.Name == "GetNestedType") &&
                    methodSymbol.ContainingType?.Name == "Type" &&
                    invocation.ArgumentList.Arguments.Count > 0)
                {
                    // Check if the first argument is a string literal matching the symbol name
                    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                    if (firstArg is LiteralExpressionSyntax lit &&
                        lit.Kind() == SyntaxKind.StringLiteralExpression &&
                        lit.Token.ValueText == symbol.Name)
                    {
                        return true;
                    }
                }

                // Check for Type.GetType calls
                if (methodSymbol != null &&
                    methodSymbol.Name == "GetType" &&
                    methodSymbol.ContainingType?.Name == "Type" &&
                    invocation.ArgumentList.Arguments.Count > 0)
                {
                    // Check if the first argument is a string literal matching the symbol's full name
                    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                    if (firstArg is LiteralExpressionSyntax lit &&
                        lit.Kind() == SyntaxKind.StringLiteralExpression)
                    {
                        var typeName = lit.Token.ValueText;
                        if (symbol is INamedTypeSymbol typeSymbol)
                        {
                            if (typeName == typeSymbol.ToDisplayString() || typeName == typeSymbol.Name)
                            {
                                return true;
                            }
                        }
                    }
                }

                // Check for Assembly.GetType calls
                if (methodSymbol != null &&
                    methodSymbol.Name == "GetType" &&
                    methodSymbol.ContainingType?.Name == "Assembly" &&
                    invocation.ArgumentList.Arguments.Count > 0)
                {
                    // Check if the first argument is a string literal matching the symbol's full name
                    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                    if (firstArg is LiteralExpressionSyntax lit &&
                        lit.Kind() == SyntaxKind.StringLiteralExpression)
                    {
                        var typeName = lit.Token.ValueText;
                        if (symbol is INamedTypeSymbol typeSymbol)
                        {
                            if (typeName == typeSymbol.ToDisplayString() || typeName == typeSymbol.Name)
                            {
                                return true;
                            }
                        }
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
                    // Also check for string arguments
                    foreach (var arg in invocation.ArgumentList.Arguments)
                    {
                        if (arg.Expression is LiteralExpressionSyntax lit &&
                            lit.Kind() == SyntaxKind.StringLiteralExpression)
                        {
                            var str = lit.Token.ValueText;
                            if (symbol is INamedTypeSymbol typeSymbol &&
                                (str == typeSymbol.Name || str == typeSymbol.ToDisplayString()))
                            {
                                return true;
                            }
                        }
                    }
                }

                // Check for Enum.Parse calls
                if (methodSymbol != null &&
                    methodSymbol.Name == "Parse" &&
                    methodSymbol.ContainingType?.Name == "Enum" &&
                    invocation.ArgumentList.Arguments.Count > 0)
                {
                    var firstArg = invocation.ArgumentList.Arguments[0].Expression;
                    if (firstArg is TypeOfExpressionSyntax tof)
                    {
                        var typeInfo = model.GetTypeInfo(tof.Type).Type;
                        if (typeInfo != null && SymbolEqualityComparer.Default.Equals(typeInfo, symbol.ContainingType))
                        {
                            return true;
                        }
                    }
                }

                // Check for Convert.ChangeType calls
                if (methodSymbol != null &&
                    methodSymbol.Name == "ChangeType" &&
                    methodSymbol.ContainingType?.Name == "Convert" &&
                    invocation.ArgumentList.Arguments.Count > 1)
                {
                    var secondArg = invocation.ArgumentList.Arguments[1].Expression;
                    if (secondArg is TypeOfExpressionSyntax tof)
                    {
                        var typeInfo = model.GetTypeInfo(tof.Type).Type;
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