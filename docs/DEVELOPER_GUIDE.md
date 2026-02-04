# DotNetPrune Developer Guide

## Architecture Overview

DotNetPrune consists of two main components:

1. **VS Code Extension** (TypeScript)
   - Provides the user interface and VS Code integration
   - Located in `src/` directory
   - Handles filtering, display, export, and user interactions

2. **C# Analyzer** (C# / .NET)
   - Performs the actual code analysis using Roslyn
   - Located in `FindUnused/` directory
   - Outputs JSON findings consumed by the extension

## Project Structure

```
dotnet-prune/
├── src/                          # TypeScript extension code
│   ├── extension.ts              # Main extension entry point
│   ├── config.ts                 # Configuration management
│   ├── filter.ts                 # Filtering logic
│   ├── diagnostics.ts            # Problems panel integration
│   ├── codeActions.ts            # Quick Fix provider
│   ├── decorator.ts              # Inline highlighting
│   └── export.ts                 # Export functionality
├── FindUnused/                   # C# analyzer
│   ├── Program.cs                # CLI entry point
│   ├── EntryPoint.cs             # Main analysis orchestration
│   ├── Analyzer.cs               # Core analysis logic
│   ├── Configuration.cs          # Analyzer configuration
│   ├── Models.cs                 # Data models
│   ├── FindingMetrics.cs         # Confidence/severity calculation
│   ├── SemanticSearch.cs         # Symbol reference searching
│   ├── TypeDiscovery.cs          # Type discovery logic
│   ├── SymbolUtilities.cs        # Symbol helper methods
│   ├── PathUtilities.cs          # Path helper methods
│   └── Utilities.cs              # General utilities
├── docs/                         # Documentation
├── resources/                    # Icons and assets
└── package.json                  # Extension manifest
```

## Building the Extension

### Prerequisites

- Node.js 16+ and npm
- .NET 10.0 SDK
- VS Code

### Build Steps

1. **Install dependencies**
   ```bash
   npm install
   ```

2. **Compile TypeScript**
   ```bash
   npm run compile
   ```

3. **Build C# analyzer**
   ```bash
   dotnet build FindUnused/FindUnused.csproj
   ```

4. **Package extension**
   ```bash
   npm run package
   ```

This creates a `.vsix` file you can install in VS Code.

## Development Workflow

### Running in Debug Mode

1. Open the project in VS Code
2. Press F5 to launch Extension Development Host
3. Open a .NET workspace in the new window
4. Set breakpoints in TypeScript code
5. Use "DotNetPrune: Run Analysis" command

### Debugging the C# Analyzer

To debug the analyzer separately:

```bash
dotnet run --project FindUnused/FindUnused.csproj -- /path/to/solution.sln
```

Attach the .NET debugger to the process.

### Testing Changes

1. Make code changes
2. Rebuild: `npm run compile` and/or `dotnet build`
3. Reload the Extension Development Host (Ctrl+R)
4. Test your changes

## Key Components

### Extension (TypeScript)

#### extension.ts
Main extension activation and command registration.

**Key responsibilities:**
- Register all commands
- Create tree view provider
- Initialize diagnostic provider, code actions, decorator, etc.
- Manage extension lifecycle

**Important functions:**
- `activate()`: Extension activation
- `deactivate()`: Cleanup

#### UnusedTreeProvider
Tree view data provider for displaying findings.

**Key responsibilities:**
- Load and parse findings from analyzer
- Organize findings hierarchically (Solution → Project → File → Finding)
- Handle filtering and search
- Manage ignored findings

**Important methods:**
- `runAnalysisAndRefresh()`: Execute analyzer and load results
- `applyFilters()`: Apply active filters to findings
- `loadFindingsFromJson()`: Parse analyzer output

#### DiagnosticProvider (diagnostics.ts)
Integrates findings with VS Code Problems panel.

**Key methods:**
- `updateDiagnostics()`: Create diagnostics from findings
- `getSeverity()`: Map finding severity to VS Code severity

#### CodeActionsProvider (codeActions.ts)
Provides Quick Fix actions for unused code.

**Key methods:**
- `provideCodeActions()`: Return available Quick Fixes
- `findFindingForLocation()`: Match finding to code location

#### InlineDecorator (decorator.ts)
Highlights unused code in the editor.

**Key methods:**
- `updateFindings()`: Update highlighted regions
- `updateDecorations()`: Apply decorations to editor
- `createHoverMessage()`: Generate rich tooltips

#### FindingsExporter (export.ts)
Exports findings to various formats.

**Key methods:**
- `exportFindings()`: Main export function
- `exportAsJson()`, `exportAsCsv()`, `exportAsMarkdown()`, `exportAsHtml()`: Format-specific export

### Analyzer (C#)

#### EntryPoint.cs
Orchestrates the analysis process.

**Key responsibilities:**
- Load solution/project workspace
- Discover types and namespaces
- Coordinate parallel project analysis
- Return results as JSON

**Important methods:**
- `RunAnalysisAsync()`: Main entry point
- `SetupWorkspaceAsync()`: Load solution into Roslyn workspace
- `IsReferenceInSolutionSource()`: Check if reference is in solution

#### Analyzer.cs
Core analysis logic for finding unused symbols.

**Key responsibilities:**
- Analyze projects for unused symbols
- Check method, property, field, parameter, and type usage
- Calculate confidence and severity

**Important methods:**
- `AnalyzeProjectAsync()`: Analyze single project
- `AnalyzeTypeAsync()`: Analyze type and its members
- `AnalyzeMethodAsync()`: Check method usage
- `AnalyzePropertyAsync()`: Check property usage
- `AnalyzeFieldAsync()`: Check field usage
- `AnalyzeTypeUsageAsync()`: Check type usage

#### FindingMetrics.cs
Calculates confidence and severity for findings.

**Key responsibilities:**
- Calculate confidence based on symbol characteristics
- Determine appropriate severity level
- Provide icons for symbol kinds

**Important methods:**
- `CalculateConfidence()`: Compute 0-100 confidence score
- `CalculateSeverity()`: Map to error/warning/information/hint
- `GetIconForSymbolKind()`: Get emoji icon for symbol

## Data Flow

1. **User triggers analysis** → Extension calls analyzer CLI
2. **Analyzer runs** → Loads solution, discovers types, analyzes symbols
3. **Analyzer outputs JSON** → Array of Finding objects
4. **Extension parses JSON** → Loads into tree provider
5. **Findings displayed** → Tree view, diagnostics, decorations
6. **User filters/searches** → Extension filters in-memory
7. **User exports** → Extension generates report file

## Adding New Features

### Adding a New Filter Type

1. Update `FilterState` interface in `filter.ts`
2. Add new filter method to `FindingFilter` class
3. Update `matches()` method to check new filter
4. Add UI command in `package.json`
5. Register command in `extension.ts`
6. Add filter method to `UnusedTreeProvider`

### Adding a New Export Format

1. Add format option in `FindingsExporter.exportFindings()`
2. Implement new `exportAsXxx()` method
3. Test with sample findings

### Adding a New Analyzer Feature

1. Update `AnalyzerConfiguration` if needed
2. Modify relevant analyzer method(s)
3. Update `Finding` model if adding new fields
4. Update extension TypeScript types to match
5. Test with sample projects

## Testing

### Manual Testing

1. Create a test .NET project with known unused code
2. Run analysis and verify findings
3. Test filtering, search, export
4. Verify Quick Fixes work correctly
5. Check Problems panel integration

### Automated Testing

The extension includes a basic test structure in `src/test/`.

To add tests:
1. Create test files in `src/test/`
2. Use VS Code's test framework
3. Run tests with `npm test`

## Code Style

### TypeScript
- Use async/await for asynchronous operations
- Prefer `const` over `let`
- Use meaningful variable names
- Add JSDoc comments for public functions
- Follow VS Code extension guidelines

### C#
- Use C# 12 features where appropriate
- Follow .NET naming conventions
- Use XML documentation comments
- Prefer LINQ for collections
- Use modern C# patterns (records, pattern matching)

## Performance Considerations

### Extension Performance
- Filter in-memory rather than re-running analysis
- Debounce decoration updates
- Cache parsed findings
- Use incremental updates where possible

### Analyzer Performance
- Run project analysis in parallel (Task.WhenAll)
- Use Roslyn's efficient symbol search
- Skip excluded patterns early
- Cache symbol lookups when possible

## Debugging Tips

### Extension Debugging
- Use VS Code debugger with breakpoints
- Check Output panel (DotNetPrune channel) for logs
- Use `console.log()` sparingly (prefer output channel)
- Test in Extension Development Host

### Analyzer Debugging
- Run analyzer standalone with test solution
- Add progress reporting for visibility
- Check JSON output structure
- Use `dotnet build` to verify compilation

### Common Issues

**Extension doesn't activate:**
- Check activation events in package.json
- Verify workspace has .NET files
- Check VS Code Output panel for errors

**Analyzer fails:**
- Verify .NET SDK is installed
- Check solution/project file is valid
- Review stderr output
- Test with simple project first

**Findings don't appear:**
- Check filter state
- Verify JSON parsing
- Check for errors in Output panel
- Test with known unused code

## Contributing

### Pull Request Guidelines

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Update documentation
6. Submit PR with clear description

### Code Review Checklist

- [ ] Code follows style guidelines
- [ ] Changes are well-tested
- [ ] Documentation updated
- [ ] No breaking changes (or clearly documented)
- [ ] Performance impact considered
- [ ] Error handling appropriate

## Release Process

1. Update version in `package.json`
2. Update CHANGELOG.md
3. Build and test: `npm run package`
4. Create GitHub release
5. Publish to VS Code Marketplace

## Resources

- [VS Code Extension API](https://code.visualstudio.com/api)
- [Roslyn API Documentation](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)
- [TypeScript Documentation](https://www.typescriptlang.org/docs/)
- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)

## Support

For questions or issues:
- GitHub Issues: https://github.com/chaluvadis/dotnet-prune/issues
- Discussions: https://github.com/chaluvadis/dotnet-prune/discussions
