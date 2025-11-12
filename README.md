# DotNetPrune

DotNetPrune analyze candidate unused code (methods, parameters, fields, properties, types) in .NET solutions using Roslyn
and produces a JSON report that can be displayed in VS Code via the DotNetPrune extension.

Usage (DotNetPrune analyzer)

- Edit src/FindUnused/Program.cs constants to point TargetPath and ReportPath, or run via CLI with a project argument.
- Build & run:
  dotnet restore
  dotnet run --project src/FindUnused -- /path/to/YourSolution.sln --report=dotnetprune-report.json

Report format
DotNetPrune writes a JSON array of findings. Each finding should include:

- Project (string)
- FilePath (string) — absolute or workspace-relative path to the source file
- Line (number) — 1-based
- SymbolKind (string) — "Method", "Parameter", "Type", etc.
- ContainingType (string)
- SymbolName (string)
- Accessibility (string)
- Remarks (string)

VS Code extension

The DotNetPrune VS Code extension displays analysis results from DotNetPrune in the Activity Bar. It can read an existing JSON report or run the DotNetPrune tool directly to generate one.

## Features

- **Tree View**: Displays findings grouped by project and file in the Activity Bar.
- **Report Reading**: Automatically loads `dotnetprune-report.json` from the workspace root, or a custom path configured in settings.
- **Analysis Execution**: If `dotNetPrune.toolCommand` is configured, run the analysis directly from VS Code.
- **File Navigation**: Click on findings to open the source file and highlight the relevant line.

## Setup

1. Place `dotnetprune-report.json` in your workspace root, or configure `dotNetPrune.reportPath` in VS Code settings for a custom location.
2. (Optional) Configure `dotNetPrune.toolCommand` in settings to enable running analysis from VS Code. Use placeholders like `${solution}`, `${reportPath}`, and `${workspaceRoot}`.
3. Install and run the extension in Extension Development Host (F5).
4. Use the DotNetPrune view in the Activity Bar to browse findings.

## Commands

- **DotNetPrune: Refresh Findings**: Reload the report from the configured path.
- **DotNetPrune: Run Analysis**: Execute the configured external tool to generate/update the report.
- **DotNetPrune: Open Report File**: Open the JSON report in the editor.
- **DotNetPrune: Clear Findings**: Clear all displayed findings.
- **DotNetPrune: Open Finding**: (Internal) Opens a finding in the editor.

Next steps

- Add code actions (e.g. mark as ignored, insert [Obsolete]) in the extension.
- Integrate DotNetPrune into CI to run on PRs and surface results.
