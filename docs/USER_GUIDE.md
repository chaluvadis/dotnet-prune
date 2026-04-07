# DotNetPrune User Guide

## Overview

DotNetPrune is a powerful VS Code extension that analyzes .NET solutions and projects to identify unused code, helping you maintain a clean and efficient codebase.

## Features

### 1. Comprehensive Code Analysis
- Detects unused methods, properties, fields, parameters, and types
- Supports .NET SDK projects, legacy projects, multi-target projects, Blazor, Xamarin, and .NET MAUI
- Analyzes entire solutions or individual projects
- Provides confidence levels for each finding

### 2. Advanced Filtering
- **Filter by Symbol Kind**: Show only specific types of unused code (methods, properties, fields, etc.)
- **Filter by Confidence**: Focus on high-confidence findings to reduce false positives
- **Filter by Project**: Narrow down results to specific projects
- **Search Findings**: Full-text search across all findings

### 3. Integrated Diagnostics
- Findings appear in the VS Code Problems panel
- Inline highlights in the editor show unused code
- Rich tooltips with detailed information

### 4. Quick Fixes
- Right-click on highlighted code to access Quick Fix actions
- Delete unused code directly from the editor
- Ignore findings you want to keep

### 5. Export Capabilities
- Export findings to JSON, CSV, Markdown, or HTML
- Generate professional reports for code reviews

## Getting Started

### Installation
1. Open VS Code
2. Go to Extensions (Ctrl+Shift+X / Cmd+Shift+X)
3. Search for "DotNetPrune"
4. Click Install

### Running Your First Analysis

1. Open a .NET workspace containing `.sln`, `.slnx`, or `.csproj` files
2. Open the Command Palette (Ctrl+Shift+P / Cmd+Shift+P)
3. Type "DotNetPrune: Run Analysis"
4. Select the solution or project to analyze
5. Wait for the analysis to complete
6. View results in the DotNetPrune panel

## Using the Extension

### Understanding the Tree View

The DotNetPrune panel organizes findings hierarchically:

```
📁 MySolution.sln
  📁 MyProject
    📁 Program.cs
      ⚙️ Method: CalculateSum (confidence: 85%)
      📦 Field: _cachedData (confidence: 95%)
    📁 Utils.cs
      🔷 Class: HelperClass (confidence: 70%)
```

- **Solutions**: Top-level organization
- **Projects**: Projects within each solution
- **Files**: Source files containing findings
- **Findings**: Individual unused code items

### Confidence Levels

Each finding includes a confidence percentage:
- **80-100%**: Very likely unused (high confidence)
- **60-79%**: Probably unused (medium confidence)
- **0-59%**: Possibly unused (low confidence)

Public symbols typically have lower confidence as they might be used externally.

### Severity Levels

Findings are categorized by severity:
- **Warning**: High-confidence private/internal unused code
- **Information**: Public symbols or medium-confidence findings
- **Hint**: Lower-confidence suggestions

## Filtering and Search

### Using Filters

Access filters from the DotNetPrune panel toolbar:

1. **Filter by Symbol Kind**
   - Click the filter icon
   - Select one or more symbol kinds (Method, Property, Field, etc.)
   - Only matching findings will be displayed

2. **Filter by Confidence**
   - Enter a minimum confidence percentage (0-100)
   - Only findings with equal or higher confidence are shown

3. **Filter by Project**
   - Select one or more projects
   - View findings only from selected projects

4. **Clear All Filters**
   - Remove all active filters to see all findings

### Searching

Use the search feature to find specific symbols:
1. Click the search icon in the toolbar
2. Enter text to search (symbol names, types, files, etc.)
3. Results update automatically

## Managing Findings

### Ignoring Findings

To ignore a finding you want to keep:
1. Right-click on the finding in the tree view
2. Select "Ignore This Finding"
3. The finding will be hidden from future analyses

### Deleting Unused Code

To remove unused code:
1. Right-click on the finding
2. Select "Delete Unused Code"
3. Confirm the deletion
4. The code will be removed from the file

**Note**: Deletions are saved immediately. Review carefully before confirming.

### Using Quick Fixes

When viewing a file with unused code:
1. Hover over the highlighted code
2. Click the lightbulb icon or press Ctrl+.
3. Select an action:
   - Delete unused code
   - Ignore this finding
   - Show details

## Export and Reporting

### Exporting Findings

1. Click the export icon in the toolbar
2. Choose a format:
   - **JSON**: Machine-readable format for automation
   - **CSV**: Import into spreadsheets
   - **Markdown**: Documentation-friendly format
   - **HTML**: Professional report with styling
3. Choose a save location
4. Open the exported file

### HTML Reports

HTML reports include:
- Summary statistics
- Findings grouped by project
- Color-coded accessibility badges
- Sortable tables
- Professional styling

## Configuration

### Accessing Settings

1. Open VS Code Settings (File > Preferences > Settings)
2. Search for "DotNetPrune"
3. Configure options

### Key Settings

#### Analysis Settings
- `dotnetprune.analysis.includePublicSymbols`: Include public symbols (default: true)
- `dotnetprune.analysis.includeInternalSymbols`: Include internal symbols (default: true)
- `dotnetprune.analysis.excludeGeneratedCode`: Exclude generated code (default: true)
- `dotnetprune.analysis.mode`: Analysis mode - "strict" or "loose" (default: "loose")

#### Filter Settings
- `dotnetprune.filter.exclusionPatterns`: Patterns to exclude (default: `**/bin/**`, `**/obj/**`)
- `dotnetprune.filter.symbolKinds`: Symbol kinds to analyze

#### UI Settings
- `dotnetprune.ui.enableInlineHighlighting`: Show inline highlights (default: true)
- `dotnetprune.ui.showConfidence`: Display confidence levels (default: true)
- `dotnetprune.ui.showSeverity`: Display severity levels (default: true)

#### Integration Settings
- `dotnetprune.integration.enableProblemsPanel`: Show in Problems panel (default: true)
- `dotnetprune.integration.enableCodeActions`: Enable Quick Fixes (default: true)

#### Performance Settings
- `dotnetprune.performance.enableCaching`: Cache analysis results (default: true)
- `dotnetprune.performance.parallelAnalysis`: Parallel project analysis (default: true)

## Tips and Best Practices

### 1. Start with High Confidence
When first analyzing a large codebase, filter by confidence ≥ 80% to focus on the most reliable findings.

### 2. Review Before Deleting
Always review findings before deleting code. Some "unused" code might be:
- Part of a public API used by external projects
- Reserved for future use
- Used via reflection

### 3. Use Filters Effectively
Combine filters to narrow down results:
- Filter by project + symbol kind to focus on specific areas
- Use search to find specific symbols

### 4. Regular Analysis
Run analysis regularly to keep your codebase clean:
- Before major releases
- After refactoring sessions
- During code reviews

### 5. Export for Team Reviews
Generate HTML reports for team discussions about code cleanup.

## Troubleshooting

### No Findings Displayed

**Issue**: Analysis completes but no findings are shown.

**Solutions**:
1. Check if filters are active - click "Clear All Filters"
2. Ensure your workspace contains .NET projects
3. Check the Output panel (DotNetPrune channel) for errors

### False Positives

**Issue**: Code marked as unused is actually used.

**Solutions**:
1. Check if code is used via reflection
2. Verify if it's part of a public API
3. Ignore the finding if it's intentional
4. Consider switching to "strict" analysis mode

### Performance Issues

**Issue**: Analysis takes too long.

**Solutions**:
1. Enable parallel analysis in settings
2. Enable caching
3. Analyze individual projects instead of entire solution
4. Exclude large generated code files

### Extension Not Working

**Issue**: Extension doesn't activate or commands are missing.

**Solutions**:
1. Ensure you're in a workspace with .NET projects
2. Reload VS Code (Developer: Reload Window)
3. Check if the .NET SDK is installed
4. Review the VS Code Output panel for errors

## Keyboard Shortcuts

While no default shortcuts are set, you can create custom shortcuts:

1. File > Preferences > Keyboard Shortcuts
2. Search for "dotnetprune"
3. Assign shortcuts to frequently used commands:
   - `dotnetprune.runAnalysis`: Run analysis
   - `dotnetprune.searchFindings`: Search findings
   - `dotnetprune.exportFindings`: Export findings

## Support

For issues, feature requests, or contributions:
- GitHub: https://github.com/chaluvadis/dotnet-prune
- Issues: https://github.com/chaluvadis/dotnet-prune/issues

## Version History

See [CHANGELOG.md](../CHANGELOG.md) for version history and updates.
