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

- Place dotnetprune-report.json in your workspace root or configure dotNetPrune.reportPath in settings.
- Install/run the extension in Extension Development Host (F5) and run "DotNetPrune: Refresh Report".

Next steps

- Add code actions (e.g. mark as ignored, insert [Obsolete]) in the extension.
- Integrate DotNetPrune into CI to run on PRs and surface results.
