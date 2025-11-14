namespace FindUnused;

/// <summary>
/// Result object for analysis operations
/// </summary>
public record AnalysisResult
{
    public bool Success { get; set; }
    public List<Finding> Findings { get; set; } = [];
    public string? ErrorMessage { get; set; }
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
