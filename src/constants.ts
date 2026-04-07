export const Messages = {
  // Analysis
  AnalysisComplete: (count: number) => `DotNetPrune: Analysis completed. ${count} findings.`,
  AnalysisFailed: (err: string) => `DotNetPrune: Analysis failed: ${err}`,
  AnalysisCancelled: "DotNetPrune: Analysis was cancelled.",
  AnalysisTimeout: "DotNetPrune: Analysis timed out. Try running on a smaller project or increasing the timeout.",
  AnalysisInProgress: "DotNetPrune: Analysis is already in progress. Please wait for it to complete.",
  AnalysisRequiresTrust: "DotNetPrune: Analysis requires a trusted workspace. Please trust this workspace to proceed.",

  // Workspace
  NoWorkspace: "DotNetPrune: Open a workspace before running analysis.",
  NoSolution: "DotNetPrune: No .sln/.slnx found in workspace. Please add a solution file.",
  NoFindings: "No findings. Run analysis to detect unused code.",

  // Cache
  CacheCleared: "DotNetPrune: Analysis cache cleared.",
  CacheUpdated: (count: number, size: string) => `Cache updated: ${count} files, ${size}`,

  // Errors
  AnalyzerNotFound: (path: string) => `Analyzer DLL not found at: ${path}`,
  AnalyzerError: (msg: string) => `Analyzer error: ${msg}`,
  InvalidOutput: "Invalid output from analyzer. Expected JSON object. See Output panel for details.",
  NoOutput: "Analyzer produced no output. Ensure a valid .sln or .csproj was selected.",

  // Export
  ExportComplete: (path: string) => `DotNetPrune: Findings exported to ${path}`,
  ExportFailed: (err: string) => `DotNetPrune: Failed to export findings: ${err}`,

  // Packages
  PackageAnalysisComplete: (projects: number, unused: number) =>
    `DotNetPrune: Package analysis complete. ${projects} project(s), ${unused} unused package reference(s) found.`,
  PackageAnalysisFailed: (err: string) => `DotNetPrune: Package analysis failed: ${err}`,
  NoPackages: "No package references found.",
  NoProjectsInSolution: "No projects found in solution.",

  // Prune
  DryRunComplete: "DotNetPrune: Dry-run complete. No changes made.",
  PruneApplied: "DotNetPrune: Prune complete. Changes have been applied.",
  PruneFailed: (err: string) => `DotNetPrune: Prune failed: ${err}`,

  // Clear
  FindingsCleared: "DotNetPrune: findings cleared.",

  // Filter
  FilterCleared: "DotNetPrune: filters cleared.",

  // View
  ViewOpened: "Opening DotNetPrune view...",
} as const;

export const Commands = {
  // Findings commands
  Refresh: "dotnetprune.refresh",
  RunAnalysis: "dotnetprune.runAnalysis",
  ClearFindings: "dotnetprune.clearFindings",
  OpenFinding: "dotnetprune.openFinding",
  CopyFilePath: "dotnetprune.copyFilePath",
  CopyProjectName: "dotnetprune.copyProjectName",
  FilterBySymbolKind: "dotnetprune.filterBySymbolKind",
  FilterByConfidence: "dotnetprune.filterByConfidence",
  FilterByProject: "dotnetprune.filterByProject",
  SearchFindings: "dotnetprune.searchFindings",
  ClearFilters: "dotnetprune.clearFilters",
  ToggleGroupByType: "dotnetprune.toggleGroupByType",
  BulkIgnore: "dotnetprune.bulkIgnore",
  BulkDelete: "dotnetprune.bulkDelete",
  ExportFindings: "dotnetprune.exportFindings",
  IgnoreFinding: "dotnetprune.ignoreFinding",
  DeleteFinding: "dotnetprune.deleteFinding",
  ShowFindingDetails: "dotnetprune.showFindingDetails",

  // Package commands
  AnalyzePackages: "dotnetprune.analyzePackages",
  RefreshPackages: "dotnetprune.refreshPackages",
  ClearPackages: "dotnetprune.clearPackages",
  PreviewPruneProject: "dotnetprune.previewPruneProject",
  ApplyPruneProject: "dotnetprune.applyPruneProject",
  PreviewPrunePackage: "dotnetprune.previewPrunePackage",
  ApplyPrunePackage: "dotnetprune.applyPrunePackage",
  ExportPruneReport: "dotnetprune.exportPruneReport",
  SuppressPackage: "dotnetprune.suppressPackage",
  OpenAllowlistConfig: "dotnetprune.openAllowlistConfig",
  GoToPackageReference: "dotnetprune.goToPackageReference",
  ReanalyzePackages: "dotnetprune.reanalyzePackages",
  FilterPackagesByConfidence: "dotnetprune.filterPackagesByConfidence",

  // Utility commands
  ClearCache: "dotnetprune.clearCache",
  ForceFullAnalysis: "dotnetprune.forceFullAnalysis",

  // Internal commands
  IgnoreFindingByLocation: "dotnetprune.ignoreFindingByLocation",
  DeleteFindingByLocation: "dotnetprune.deleteFindingByLocation",
} as const;

export const FileExtensions = {
  DotNet: [".cs", ".sln", ".slnx", ".csproj"],
  Solution: [".sln", ".slnx"],
  Project: [".csproj"],
} as const;

export const Defaults = {
  AnalysisTimeout: 300000, // 5 minutes
  MaxFindings: 1000,
  ConfidenceThresholds: {
    high: 80,
    medium: 50,
  },
} as const;
