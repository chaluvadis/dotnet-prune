import * as vscode from "vscode";

export interface DotNetPruneConfig {
  analysis: {
    includePublicSymbols: boolean;
    includeInternalSymbols: boolean;
    excludeGeneratedCode: boolean;
    mode: "strict" | "loose";
    detectDeadCode: boolean;
    detectTodoComments: boolean;
  };
  filter: {
    exclusionPatterns: string[];
    inclusionPatterns: string[];
    symbolKinds: string[];
  };
  ui: {
    enableInlineHighlighting: boolean;
    showConfidence: boolean;
    showSeverity: boolean;
  };
  performance: {
    enableCaching: boolean;
    parallelAnalysis: boolean;
    enableIncrementalAnalysis: boolean;
  };
  integration: {
    enableProblemsPanel: boolean;
    enableCodeActions: boolean;
  };
  prune: {
    runRestoreAfterPrune: boolean;
    runBuildAfterPrune: boolean;
    allowlistPath: string;
  };
  autoRefreshOnSave: boolean;
  maxFindings: number;
  excludeGlobs: string[];
  analyzerPath: string;
  logLevel: "off" | "error" | "warn" | "info" | "debug";
}

export function getConfig(): DotNetPruneConfig {
  const config = vscode.workspace.getConfiguration("dotnetprune");

  return {
    analysis: {
      includePublicSymbols: config.get("analysis.includePublicSymbols", true),
      includeInternalSymbols: config.get("analysis.includeInternalSymbols", true),
      excludeGeneratedCode: config.get("analysis.excludeGeneratedCode", true),
      mode: config.get("analysis.mode", "loose"),
      detectDeadCode: config.get("analysis.detectDeadCode", false),
      detectTodoComments: config.get("analysis.detectTodoComments", false),
    },
    filter: {
      exclusionPatterns: config.get("filter.exclusionPatterns", [
        "**/bin/**",
        "**/obj/**",
        "**/node_modules/**",
      ]),
      inclusionPatterns: config.get("filter.inclusionPatterns", [
        "**/*.cs",
        "**/*.sln",
        "**/*.csproj",
      ]),
      symbolKinds: config.get("filter.symbolKinds", [
        "Method",
        "Property",
        "Field",
        "Parameter",
        "Type",
      ]),
    },
    ui: {
      enableInlineHighlighting: config.get("ui.enableInlineHighlighting", true),
      showConfidence: config.get("ui.showConfidence", true),
      showSeverity: config.get("ui.showSeverity", true),
    },
    performance: {
      enableCaching: config.get("performance.enableCaching", true),
      parallelAnalysis: config.get("performance.parallelAnalysis", true),
      enableIncrementalAnalysis: config.get("performance.enableIncrementalAnalysis", true),
    },
    integration: {
      enableProblemsPanel: config.get("integration.enableProblemsPanel", true),
      enableCodeActions: config.get("integration.enableCodeActions", true),
    },
    prune: {
      runRestoreAfterPrune: config.get("prune.runRestoreAfterPrune", false),
      runBuildAfterPrune: config.get("prune.runBuildAfterPrune", false),
      allowlistPath: config.get("prune.allowlistPath", ""),
    },
    autoRefreshOnSave: config.get("autoRefreshOnSave", false),
    maxFindings: config.get("maxFindings", 1000),
    excludeGlobs: config.get("excludeGlobs", []),
    analyzerPath: config.get("analyzerPath", ""),
    logLevel: config.get("logLevel", "info"),
  };
}
