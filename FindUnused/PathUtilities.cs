namespace FindUnused;

/// <summary>
/// Utility methods for path handling and file system operations
/// </summary>
public static class PathUtilities
{
    /// <summary>
    /// Determine if the given file path is located inside an excluded folder
    /// </summary>
    public static bool IsPathExcluded(string? filePath, HashSet<string> exclusionPatterns)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var low = filePath.Replace('\\', '/').ToLowerInvariant();
        return exclusionPatterns.Any(low.Contains);
    }

    /// <summary>
    /// Check if a syntax tree is excluded based on its file path
    /// </summary>
    public static bool SourceTreeIsExcluded(SyntaxTree? tree, HashSet<string> exclusionPatterns)
    {
        if (tree == null) return false;
        return IsPathExcluded(tree.FilePath, exclusionPatterns);
    }

    /// <summary>
    /// Get a file path for display in findings
    /// </summary>
    public static string GetDisplayPathForDocument(Document? doc, SyntaxTree? tree, Project? project, Solution? solution)
    {
        // Similar to original, but using Path.GetRelativePath etc.
        // Keep the logic as is for now
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
                    catch { }
                }

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
                    catch { }
                }

                if (!string.IsNullOrEmpty(doc.FilePath))
                    return Path.GetFileName(doc.FilePath);
            }

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
        catch { }

        return "(generated)";
    }

    /// <summary>
    /// Compute a short DisplayName for a document/tree
    /// </summary>
    public static string GetDisplayNameForDocument(Document? doc, SyntaxTree? tree)
    {
        try
        {
            if (doc != null && !string.IsNullOrEmpty(doc.FilePath))
                return Path.GetFileName(doc.FilePath);
            if (tree != null && !string.IsNullOrEmpty(tree.FilePath))
                return Path.GetFileName(tree.FilePath);
        }
        catch { }

        return "(generated)";
    }

    /// <summary>
    /// Get an absolute file path for the given document/tree
    /// </summary>
    public static string? GetFullPathForDocument(Document? doc, SyntaxTree? tree)
    {
        if (doc != null && !string.IsNullOrEmpty(doc.FilePath))
            return Path.GetFullPath(doc.FilePath);
        if (tree != null && !string.IsNullOrEmpty(tree.FilePath))
            return Path.GetFullPath(tree.FilePath);
        return null;
    }

    /// <summary>
    /// Get absolute project file path
    /// </summary>
    public static string? GetProjectFilePath(Project? project, Document? doc)
    {
        if (project?.FilePath != null && !string.IsNullOrEmpty(project.FilePath)) return Path.GetFullPath(project.FilePath);
        if (doc?.Project?.FilePath != null && !string.IsNullOrEmpty(doc.Project.FilePath)) return Path.GetFullPath(doc.Project.FilePath);
        return null;
    }
}