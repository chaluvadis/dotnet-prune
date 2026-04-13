import * as vscode from "vscode";
import * as fs from "node:fs";
import * as path from "node:path";
import { spawn } from "node:child_process";
import { getConfig } from "./config";
import { FindingFilter } from "./filter";
import { DiagnosticProvider } from "./diagnostics";
import { CodeActionsProvider } from "./codeActions";
import { FindingsExporter } from "./export";
import { InlineDecorator } from "./decorator";
import {
  PackageInventoryProvider,
  PackageGroupTreeItem,
  PackageTreeItem,
  AllowlistWriter,
  CsprojNavigator,
} from "./packageInventory";
import type { Finding, MetricsData } from "./types";
import { PathResolver, getOrCreatePathResolver, clearGlobalPathResolver } from "./pathResolver";
import { PruneExecutor } from "./pruneExecutor";
import { FileHashCache, clearGlobalCache, getOrCreateCache } from "./cache";

const LOG_LEVELS: Record<string, number> = {
  debug: 0,
  info: 1,
  warn: 2,
  error: 3,
};

let outputChannel: vscode.OutputChannel | undefined;

const getLogLevel = (): number => {
  const config = vscode.workspace.getConfiguration("dotnetprune");
  const level = config.get<string>("logLevel", "info");
  return LOG_LEVELS[level] ?? LOG_LEVELS.info;
};

const getMaxFindings = (): number => {
  const config = vscode.workspace.getConfiguration("dotnetprune");
  return config.get<number>("maxFindings", 1000);
};

const getAnalyzerPath = (): string | undefined => {
  const config = vscode.workspace.getConfiguration("dotnetprune");
  return config.get<string>("analyzerPath", "");
};

const getAutoRefreshOnSave = (): boolean => {
  const config = vscode.workspace.getConfiguration("dotnetprune");
  return config.get<boolean>("autoRefreshOnSave", false);
};

const getConfidenceLevel = (): string => {
  const cfg = getConfig();
  return cfg.filter.confidenceLevel;
};

const getAnalysisTimeout = (): number => {
  const config = vscode.workspace.getConfiguration("dotnetprune");
  return config.get<number>("analysisTimeout", 300000);
};

export function activate(context: vscode.ExtensionContext) {
  const provider = new UnusedTreeProvider(context);
  const metricsProvider = new MetricsTreeProvider(context, provider);
  const treeView = vscode.window.createTreeView("dotnetprune-findings", {
    treeDataProvider: provider,
    showCollapseAll: true,
    canSelectMany: true,
  });
  treeView.onDidChangeSelection((e) => {
    if (e.selection.length === 1 && e.selection[0] instanceof FindingTreeItem) {
      const item = e.selection[0] as FindingTreeItem;
      provider.openFinding(item.finding);
    }
  });
  const metricsTreeView = vscode.window.createTreeView("dotnetprune-metrics", {
    treeDataProvider: metricsProvider,
    showCollapseAll: true,
  });

  // Package inventory view (Phase 1 read-only)
  const packageProvider = new PackageInventoryProvider();
  const packageTreeView = vscode.window.createTreeView(
    "dotnetprune-packages",
    {
      treeDataProvider: packageProvider,
      showCollapseAll: true,
    }
  );

  // Initialize new features
  const diagnosticProvider = new DiagnosticProvider();
  const codeActionsProvider = new CodeActionsProvider();
  const exporter = new FindingsExporter();
  const decorator = new InlineDecorator();

  // Register code actions provider
  const codeActionsRegistration = vscode.languages.registerCodeActionsProvider(
    { language: "csharp", scheme: "file" },
    codeActionsProvider,
    {
      providedCodeActionKinds: [vscode.CodeActionKind.QuickFix],
    }
  );

  // Auto-refresh on save when enabled in settings
  const onSaveListener = vscode.workspace.onDidSaveTextDocument((doc) => {
    const cfg = getConfig();
    if (
      cfg.autoRefreshOnSave &&
      (doc.languageId === "csharp" || doc.fileName.endsWith(".cs"))
    ) {
      provider.refresh();
    }
  });

  context.subscriptions.push(
    treeView,
    packageTreeView,
    diagnosticProvider,
    codeActionsRegistration,
    decorator,
    onSaveListener,
    vscode.commands.registerCommand("dotnetprune.refresh", () =>
      provider.refresh()
    ),
    vscode.commands.registerCommand("dotnetprune.runAnalysis", async () => {
      await provider.runAnalysisAndRefresh();
      metricsProvider.refresh();
    }),
    vscode.commands.registerCommand("dotnetprune.clearFindings", () =>
      provider.clear()
    ),
    vscode.commands.registerCommand("dotnetprune.clearCache", () => {
      clearGlobalCache();
      vscode.window.showInformationMessage("DotNetPrune: Analysis cache cleared.");
    }),
    vscode.commands.registerCommand("dotnetprune.forceFullAnalysis", async () => {
      clearGlobalCache();
      await provider.runAnalysisAndRefresh();
    }),
    vscode.commands.registerCommand(
      "dotnetprune.openFinding",
      async (item: FindingTreeItem) => {
        if (!item) return;
        await provider.openFinding(item.finding);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.copyFilePath",
      async (item: FileTreeItem) => {
        if (!item || !item.filePath) return;
        await vscode.env.clipboard.writeText(item.filePath);
        vscode.window.setStatusBarMessage(
          `DotNetPrune: ${item.filePath} path copied to clipboard`,
          3000
        );
      }
    ),
    vscode.commands.registerCommand("dotnetprune.copyProjectName", async (item: ProjectTreeItem) => {
        if (!item || !item.label) return;
        await vscode.env.clipboard.writeText(item.label);
        vscode.window.setStatusBarMessage(
          `DotNetPrune: ${item.label} name copied to clipboard`,
          3000
        );
      }
    ),
    // New filter commands
    vscode.commands.registerCommand("dotnetprune.filterBySymbolKind", async () => {
      await provider.filterBySymbolKind();
    }),
    vscode.commands.registerCommand("dotnetprune.filterByConfidence", async () => {
      await provider.filterByConfidence();
    }),
    vscode.commands.registerCommand("dotnetprune.filterByProject", async () => {
      await provider.filterByProject();
    }),
    vscode.commands.registerCommand("dotnetprune.searchFindings", async () => {
      await provider.searchFindings();
    }),
    vscode.commands.registerCommand("dotnetprune.clearFilters", () => {
      provider.clearFilters();
    }),
    vscode.commands.registerCommand("dotnetprune.toggleGroupByType", () => {
      provider.toggleGroupByType();
    }),
    vscode.commands.registerCommand("dotnetprune.toggleViewMode", () => {
      provider.toggleViewMode();
    }),
    vscode.commands.registerCommand("dotnetprune.toggleShowDeletable", () => {
      provider.toggleShowDeletable();
    }),
    // Export command
    vscode.commands.registerCommand("dotnetprune.exportFindings", async () => {
      await exporter.exportFindings(provider.getAllFindings());
    }),
    // Finding actions
    vscode.commands.registerCommand(
      "dotnetprune.ignoreFinding",
      async (item: FindingTreeItem) => {
        if (!item) return;
        await provider.ignoreFinding(item.finding);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.deleteFinding",
      async (item: FindingTreeItem) => {
        if (!item) return;
        await provider.deleteFinding(item.finding);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.ignoreFindingByLocation",
      async (finding: Finding) => {
        await provider.ignoreFinding(finding);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.deleteFindingByLocation",
      async (finding: Finding) => {
        await provider.deleteFinding(finding);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.showFindingDetails",
      async (finding: Finding) => {
        provider.showFindingDetails(finding);
      }
    ),
    // Bulk actions
    vscode.commands.registerCommand("dotnetprune.bulkIgnore", async () => {
      await provider.bulkIgnore();
    }),
    vscode.commands.registerCommand("dotnetprune.bulkDelete", async () => {
      await provider.bulkDelete();
    }),
    // Package inventory commands (Phase 1 read-only)
    vscode.commands.registerCommand(
      "dotnetprune.analyzePackages",
      async () => {
        await packageProvider.runAnalysis(false);
      }
    ),
    vscode.commands.registerCommand("dotnetprune.refreshPackages", () => {
      packageProvider.refresh();
    }),
    vscode.commands.registerCommand("dotnetprune.clearPackages", () => {
      packageProvider.clear();
    }),
    // Phase 2: Prune commands
    vscode.commands.registerCommand(
      "dotnetprune.previewPruneProject",
      async (item: PackageGroupTreeItem) => {
        if (!item || !item.projectInfo.prunePlan) {
          vscode.window.showInformationMessage(
            "DotNetPrune: No prune plan available. Run Analyze Packages first."
          );
          return;
        }
        await runPruneFlow(packageProvider, item.projectInfo.prunePlan, true);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.applyPruneProject",
      async (item: PackageGroupTreeItem) => {
        if (!item || !item.projectInfo.prunePlan) {
          vscode.window.showInformationMessage(
            "DotNetPrune: No prune plan available. Run Analyze Packages first."
          );
          return;
        }
        await runPruneFlow(packageProvider, item.projectInfo.prunePlan, false);
        packageProvider.refresh();
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.previewPrunePackage",
      async (item: PackageTreeItem) => {
        if (!item || !item.isUnused || !item.confidence || !item.projectPath) return;
        const plan = {
          projectName: path.basename(item.projectPath, ".csproj"),
          projectPath: item.projectPath,
          entries: [{ pkg: item.pkg, confidence: item.confidence, reason: item.pruneReason ?? "" }],
        };
        await runPruneFlow(packageProvider, plan, true);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.applyPrunePackage",
      async (item: PackageTreeItem) => {
        if (!item || !item.isUnused || !item.confidence || !item.projectPath) return;
        if (item.confidence === "Blocked") {
          vscode.window.showWarningMessage(
            `DotNetPrune: ${item.pkg.include} is Blocked and cannot be removed automatically.`
          );
          return;
        }
        const plan = {
          projectName: path.basename(item.projectPath, ".csproj"),
          projectPath: item.projectPath,
          entries: [{ pkg: item.pkg, confidence: item.confidence, reason: item.pruneReason ?? "" }],
        };
        await runPruneFlow(packageProvider, plan, false);
        packageProvider.refresh();
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.exportPruneReport",
      async () => {
        await exportLastPruneReport();
      }
    ),
    // Phase 3: Trust controls
    vscode.commands.registerCommand(
      "dotnetprune.suppressPackage",
      async (item: PackageTreeItem) => {
        if (!item || !item.isUnused) return;
        const workspaceRoot = packageProvider.getWorkspaceRoot();
        if (!workspaceRoot) {
          vscode.window.showErrorMessage(
            "DotNetPrune: Open a workspace to manage the allowlist."
          );
          return;
        }
        try {
          AllowlistWriter.add(workspaceRoot, item.pkg.include);
          vscode.window.showInformationMessage(
            `DotNetPrune: "${item.pkg.include}" added to allowlist. Re-analyzing...`
          );
          packageProvider.refresh();
        } catch (err) {
          vscode.window.showErrorMessage(
            `DotNetPrune: Failed to update allowlist: ${err}`
          );
        }
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.openAllowlistConfig",
      async () => {
        const workspaceRoot = packageProvider.getWorkspaceRoot();
        if (!workspaceRoot) {
          vscode.window.showErrorMessage(
            "DotNetPrune: Open a workspace to manage the allowlist."
          );
          return;
        }
        const allowlistPath = AllowlistWriter.getPath(workspaceRoot);
        // Create the file with default content if it doesn't exist
        const fs = await import("node:fs");
        if (!fs.existsSync(allowlistPath)) {
          fs.writeFileSync(
            allowlistPath,
            JSON.stringify({ allowlist: [] }, null, 2) + "\n",
            "utf-8"
          );
        }
        const doc = await vscode.workspace.openTextDocument(allowlistPath);
        await vscode.window.showTextDocument(doc);
      }
    ),
    // Phase 3: Navigation — jump to PackageReference in .csproj
    vscode.commands.registerCommand(
      "dotnetprune.goToPackageReference",
      async (item: PackageTreeItem) => {
        if (!item || !item.projectPath) return;
        try {
          const fs = await import("node:fs");
          const content = fs.readFileSync(item.projectPath, "utf-8");
          const line = CsprojNavigator.findPackageReferenceLine(
            content,
            item.pkg.include
          );
          const doc = await vscode.workspace.openTextDocument(item.projectPath);
          const editor = await vscode.window.showTextDocument(doc);
          if (line !== undefined) {
            const position = new vscode.Position(line, 0);
            editor.selection = new vscode.Selection(position, position);
            editor.revealRange(
              new vscode.Range(position, position),
              vscode.TextEditorRevealType.InCenter
            );
          }
        } catch (err) {
          vscode.window.showErrorMessage(
            `DotNetPrune: Could not open .csproj file: ${err}`
          );
        }
      }
    ),
    // Phase 3: Re-analyze now
    vscode.commands.registerCommand(
      "dotnetprune.reanalyzePackages",
      () => {
        packageProvider.refresh();
      }
    ),
    // Phase 3: Filter packages tree by confidence
    vscode.commands.registerCommand(
      "dotnetprune.filterPackagesByConfidence",
      async () => {
        const current = packageProvider.getConfidenceFilter();
        const items = [
          { label: "$(list-unordered) All", value: "All" as const, description: current === "All" ? "active" : "" },
          { label: "$(trash) High confidence only", value: "High" as const, description: current === "High" ? "active" : "" },
          { label: "$(question) Medium confidence only", value: "Medium" as const, description: current === "Medium" ? "active" : "" },
          { label: "$(lock) Blocked only", value: "Blocked" as const, description: current === "Blocked" ? "active" : "" },
        ];
        const sel = await vscode.window.showQuickPick(items, {
          placeHolder: "Filter unused packages by confidence level",
        });
        if (!sel) return;
        packageProvider.setConfidenceFilter(sel.value);
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.exportMetrics",
      async () => {
        await metricsProvider.exportMetrics();
      }
    )
  );

  // Phase 3: File watcher — incremental re-analysis when .csproj or allowlist changes
  const csprojWatcher = vscode.workspace.createFileSystemWatcher(
    "**/*.csproj",
    false, // create
    false, // change
    false  // delete
  );
  const allowlistWatcher = vscode.workspace.createFileSystemWatcher(
    "**/.dotnet-prune.json"
  );

  const onRelevantFileChange = () => {
    // Only trigger if the packages view has data (avoid spurious refreshes on first open)
    if (packageProvider.getInventories().length > 0) {
      packageProvider.refresh();
    }
  };

  csprojWatcher.onDidChange(onRelevantFileChange);
  csprojWatcher.onDidCreate(onRelevantFileChange);
  csprojWatcher.onDidDelete(onRelevantFileChange);
  allowlistWatcher.onDidChange(onRelevantFileChange);
  allowlistWatcher.onDidCreate(onRelevantFileChange);

  context.subscriptions.push(csprojWatcher, allowlistWatcher);

  // Pass providers to tree provider
  provider.setProviders(diagnosticProvider, codeActionsProvider, decorator);

  // initial load
  provider.refresh();
}

export function deactivate() {
  if (outputChannel) {
    outputChannel.dispose();
    outputChannel = undefined;
  }
}

// ─── Phase 2: Prune Helpers ───────────────────────────────────────────────────

/** Module-level storage for the last prune report (in-memory, resets on reload). */
let lastPruneReport: import("./pruneExecutor").PruneReport | undefined;

import type { ProjectPrunePlan } from "./packageInventory";

/**
 * Run a prune flow for a given plan. In dry-run mode only shows what would
 * happen. In apply mode asks for confirmation before executing.
 */
async function runPruneFlow(
  packageProvider: PackageInventoryProvider,
  plan: ProjectPrunePlan,
  dryRun: boolean
): Promise<void> {
  const pruneable = plan.entries.filter((e) => e.confidence !== "Blocked");
  const blocked = plan.entries.filter((e) => e.confidence === "Blocked");

  if (pruneable.length === 0) {
    vscode.window.showInformationMessage(
      `DotNetPrune: No pruneable packages for ${plan.projectName} — all are Blocked or allowlisted.`
    );
    return;
  }

  const cfg = getConfig();
  const outputChannel = packageProvider.getOutputChannel();
  outputChannel.show(false);

  if (dryRun) {
    // Show dry-run preview
    outputChannel.appendLine(`\n[DRY-RUN] Prune preview for ${plan.projectName}:`);
    for (const entry of pruneable) {
      outputChannel.appendLine(
        `  - ${entry.pkg.include}${entry.pkg.version ? ` (${entry.pkg.version})` : ""} [${entry.confidence}]: ${entry.reason}`
      );
    }
    if (blocked.length > 0) {
      outputChannel.appendLine(`  Skipped (Blocked): ${blocked.map((e) => e.pkg.include).join(", ")}`);
    }
    outputChannel.appendLine(`  Total: ${pruneable.length} would be removed, ${blocked.length} skipped.`);

    const executor = new PruneExecutor(outputChannel, true);
    const outcome = await executor.executeProjectPlan(
      { ...plan, entries: plan.entries },
      { runRestore: false, runBuild: false }
    );
    lastPruneReport = PruneExecutor.buildReport([outcome], true);
    outputChannel.appendLine(PruneExecutor.formatReportSummary(lastPruneReport));
    return;
  }

  // Apply: require confirmation
  const packageList = pruneable
    .map((e) => `• ${e.pkg.include}${e.pkg.version ? ` (${e.pkg.version})` : ""} [${e.confidence}]`)
    .join("\n");
  const confirmMessage = `Remove ${pruneable.length} package(s) from ${plan.projectName}?\n\n${packageList}`;
  const confirmed = await vscode.window.showWarningMessage(
    confirmMessage,
    { modal: true },
    "Yes, Remove"
  );
  if (confirmed !== "Yes, Remove") return;

  const executor = new PruneExecutor(outputChannel, false);
  const outcome = await vscode.window.withProgress(
    {
      location: vscode.ProgressLocation.Notification,
      title: `DotNetPrune: Pruning ${plan.projectName}...`,
      cancellable: false,
    },
    async () =>
      executor.executeProjectPlan(plan, {
        runRestore: cfg.prune.runRestoreAfterPrune,
        runBuild: cfg.prune.runBuildAfterPrune,
      })
  );

  lastPruneReport = PruneExecutor.buildReport([outcome], false);
  const summary = PruneExecutor.formatReportSummary(lastPruneReport);
  outputChannel.appendLine(summary);

  const removed = outcome.packages.filter((p) => p.status === "removed").length;
  const failed = outcome.packages.filter((p) => p.status === "failed").length;
  const skipped = outcome.packages.filter((p) => p.status === "skipped").length;

  const summaryParts: string[] = [`Removed ${removed}`];
  if (skipped > 0) summaryParts.push(`skipped ${skipped}`);
  if (failed > 0) summaryParts.push(`failed ${failed}`);
  const summaryMsg = `DotNetPrune: ${summaryParts.join(", ")} package(s) from ${plan.projectName}.`;

  if (failed > 0) {
    vscode.window.showWarningMessage(
      summaryMsg + " See Output for details.",
      "Re-analyze Now"
    ).then((action) => {
      if (action === "Re-analyze Now") {
        packageProvider.refresh();
      }
    });
  } else {
    vscode.window.showInformationMessage(
      summaryMsg,
      "Re-analyze Now"
    ).then((action) => {
      if (action === "Re-analyze Now") {
        packageProvider.refresh();
      }
    });
  }
}

/** Export the last prune report to a JSON file in the workspace root. */
async function exportLastPruneReport(): Promise<void> {
  if (!lastPruneReport) {
    vscode.window.showInformationMessage(
      "DotNetPrune: No prune report available. Run a preview or apply operation first."
    );
    return;
  }
  const filePath = await PruneExecutor.saveReport(lastPruneReport);
  if (filePath) {
    const open = await vscode.window.showInformationMessage(
      `DotNetPrune: Report saved to ${filePath}`,
      "Open File"
    );
    if (open === "Open File") {
      const doc = await vscode.workspace.openTextDocument(filePath);
      await vscode.window.showTextDocument(doc);
    }
  } else {
    vscode.window.showErrorMessage(
      "DotNetPrune: Failed to save prune report. Check that a workspace folder is open."
    );
  }
}

// Helper function to get appropriate icon for symbol kinds (fallback when backend doesn't provide icon)
const getIconForSymbolKind = (symbolKind: string): vscode.ThemeIcon => {
  const kind = symbolKind.toLowerCase();

  if (kind.includes('class') || kind.includes('type')) {
    return new vscode.ThemeIcon("symbol-class");
  }

  if (kind.includes('interface')) {
    return new vscode.ThemeIcon("symbol-interface");
  }

  if (kind.includes('method') || kind.includes('function')) {
    return new vscode.ThemeIcon("symbol-method");
  }

  if (kind.includes('property')) {
    return new vscode.ThemeIcon("symbol-property");
  }

  if (kind.includes('field') || kind.includes('variable')) {
    return new vscode.ThemeIcon("symbol-field");
  }

  if (kind.includes('parameter') || kind.includes('param')) {
    return new vscode.ThemeIcon("symbol-parameter");
  }

  if (kind.includes('enum')) {
    return new vscode.ThemeIcon("symbol-enum");
  }

  if (kind.includes('struct')) {
    return new vscode.ThemeIcon("symbol-structure");
  }

  if (kind.includes('namespace')) {
    return new vscode.ThemeIcon("symbol-namespace");
  }

  if (kind.includes('event')) {
    return new vscode.ThemeIcon("symbol-event");
  }

  return new vscode.ThemeIcon("warning");
}

const getWorkspaceRootPath = (): string => {
  const workspaceFolders = vscode.workspace.workspaceFolders;
  if (!workspaceFolders || workspaceFolders.length === 0) {
    return ".";
  }
  return workspaceFolders[0].uri.fsPath;
};

const matchesExcludeGlobs = (filePath: string, globs: string[]): boolean => {
  if (globs.length === 0) return false;
  const relativePath = path.relative(getWorkspaceRootPath(), filePath);
  for (const glob of globs) {
    if (glob.includes('*') || glob.includes('?')) {
      const regex = new RegExp(
        '^' + glob.replace(/\*\*/g, '.*').replace(/\*/g, '[^/]*').replace(/\?/g, '.') + '$'
      );
      if (regex.test(relativePath) || regex.test(filePath)) {
        return true;
      }
    } else if (relativePath.includes(glob) || filePath.includes(glob)) {
      return true;
    }
  }
  return false;
};

class UnusedTreeProvider implements vscode.TreeDataProvider<TreeItemBase> {
  private _onDidChangeTreeData: vscode.EventEmitter<TreeItemBase | undefined> =
    new vscode.EventEmitter<TreeItemBase | undefined>();
  readonly onDidChangeTreeData: vscode.Event<TreeItemBase | undefined> =
    this._onDidChangeTreeData.event;
  private findings: Finding[] = [];
  private allFindings: Finding[] = []; // Unfiltered findings
  private groupedBySolution: Map<string, Map<string, Map<string, Finding[]>>> =
    new Map();
  private solutionFiles: Map<string, string> = new Map(); // solutionName -> solutionFilePath
  private projectToSolutionMap: Map<string, string> = new Map(); // projectName -> solutionName
  private filter: FindingFilter = new FindingFilter();
  private diagnosticProvider?: DiagnosticProvider;
  private codeActionsProvider?: CodeActionsProvider;
  private decorator?: InlineDecorator;
  private isAnalysisRunning = false;
  private analysisQueue: Promise<void> = Promise.resolve();
  private fileCache?: FileHashCache; // Cache for incremental analysis
  private pathResolver: PathResolver; // Consolidated path resolution
  private groupByType: boolean = false;
  private viewMode: "compact" | "full" = "compact";
  private showOnlyDeletable: boolean = false;

  constructor(private context: vscode.ExtensionContext) {
    // Initialize file hash cache for incremental analysis
    this.fileCache = new FileHashCache(getWorkspaceRootPath());
    // Initialize path resolver
    this.pathResolver = getOrCreatePathResolver(getWorkspaceRootPath());
  }

  setProviders(
    diagnosticProvider: DiagnosticProvider,
    codeActionsProvider: CodeActionsProvider,
    decorator: InlineDecorator
  ): void {
    this.diagnosticProvider = diagnosticProvider;
    this.codeActionsProvider = codeActionsProvider;
    this.decorator = decorator;
  }

  getAllFindings(): Finding[] {
    return this.allFindings.slice();
  }

  getFindingId(finding: Finding): string {
    return `${finding.FilePath}:${finding.Line}:${finding.SymbolName}`;
  }

  async bulkIgnore(): Promise<void> {
    const selected = vscode.window.activeTextEditor?.selection;
    if (!selected) return;
    const selectedFindings = this.findings.filter(f => 
      f.FilePath === vscode.window.activeTextEditor?.document.uri.fsPath &&
      f.Line >= selected.start.line + 1 &&
      f.Line <= selected.end.line + 1
    );
    for (const f of selectedFindings) {
      this.filter.addIgnored(f);
    }
    this.applyFilters();
    await this.context.workspaceState.update("ignoredFindings", Array.from(this.filter.getIgnoredFindings()));
  }

  async bulkDelete(): Promise<void> {
    const selected = vscode.window.activeTextEditor?.selection;
    if (!selected) return;
    
    // Get FindingTreeItem from the tree view
    const selectedTreeItems = await vscode.commands.executeCommand<FindingTreeItem[]>(
      'vscode.executeTreeItemPicker'
    );
    
    if (!selectedTreeItems || selectedTreeItems.length === 0) {
      vscode.window.showWarningMessage("DotNetPrune: No findings selected.");
      return;
    }
    
    await this.deleteFindings(selectedTreeItems);
  }

  refresh(): void {
    this.runAnalysisAndRefresh(true).catch((err) => {
      vscode.window.showErrorMessage(
        `DotNetPrune: Failed to run analysis: ${err}`
      );
    });
  }

  clear(): void {
    this.findings = [];
    this.allFindings = [];
    this.groupedBySolution.clear();
    this.solutionFiles.clear();
    this.projectToSolutionMap.clear();
    this.filter.clearAll();
    this._onDidChangeTreeData.fire(undefined);
    
    // Clear diagnostics and decorations
    if (this.diagnosticProvider) {
      this.diagnosticProvider.clear();
    }
    if (this.decorator) {
      this.decorator.clear();
    }
    
    vscode.window.showInformationMessage("DotNetPrune: findings cleared.");
  }

  async runAnalysisAndRefresh(silent: boolean = false): Promise<void> {
    // Queue analysis - wait for any in-progress analysis to complete first
    this.analysisQueue = this.analysisQueue.then(async () => {
      await this.performAnalysis(silent);
    });
    await this.analysisQueue;
  }

  private async performAnalysis(silent: boolean): Promise<void> {
    // Workspace trust guard
    if (!vscode.workspace.isTrusted) {
      if (!silent) {
        vscode.window.showWarningMessage(
          "DotNetPrune: Analysis requires a trusted workspace. Please trust this workspace to proceed."
        );
      }
      return;
    }

    // Run-lock: prevent overlapping analysis runs
    if (this.isAnalysisRunning) {
      if (!silent) {
        vscode.window.showWarningMessage(
          "DotNetPrune: Analysis is already in progress. Please wait for it to complete."
        );
      }
      return;
    }

    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
      vscode.window.showErrorMessage(
        "DotNetPrune: Open a workspace before running analysis."
      );
      this.appendToOutput("DotNetPrune: Workspace is not trusted. Analysis aborted.", "warn");
      return;
    }

    this.isAnalysisRunning = true;
    try {
      // discover solution/csproj files (excluding build folders)
      const excludedFolders = "**/{bin,debug,obj,release,nuget,bin/**,debug/**,obj/**,release/**,nuget/**}/**";
      const slnxCandidates = await vscode.workspace.findFiles(
      "**/*.slnx",
      `${excludedFolders},**/node_modules/**`,
      10
    );
    const slnCandidates = await vscode.workspace.findFiles(
      "**/*.sln",
      `${excludedFolders},**/node_modules/**`,
      10
    );
    const csprojCandidates = await vscode.workspace.findFiles(
      "**/*.csproj",
      `${excludedFolders},**/node_modules/**`,
      20
    );

    const allCandidates = [
      ...slnxCandidates,
      ...slnCandidates,
      ...csprojCandidates,
    ];
    if (allCandidates.length === 0) {
      vscode.window.showWarningMessage(
        "DotNetPrune: No .sln/.slnx/.csproj found in workspace. Please add a project/solution to the workspace."
      );
      return;
    }

    let chosen = allCandidates[0];
    if (allCandidates.length > 1 && !silent) {
      const picks = allCandidates.map((u) => ({
        label: path.relative(getWorkspaceRootPath(), u.fsPath),
        uri: u,
      }));
      const sel = await vscode.window.showQuickPick(picks, {
        placeHolder: "Select solution or project to analyze",
      });
      if (!sel) return;
      chosen = sel.uri;
    }

    // Validate and sanitize the chosen file path
    const chosenPath = chosen.fsPath;
    if (!this.isValidFilePath(chosenPath)) {
      vscode.window.showErrorMessage(
        "DotNetPrune: Invalid file path selected for analysis."
      );
      return;
    }

    const dllPath = this.getDllPath();
    if (!dllPath || !fs.existsSync(dllPath)) {
      this.appendToOutput("Analyzer DLL not found at: " + dllPath, "error");
      vscode.window.showErrorMessage(
        "DotNetPrune: Analyzer not found. Please ensure the extension is properly installed.",
        "Open Output"
      ).then((choice) => {
        if (choice === "Open Output") {
          outputChannel?.show(true);
        }
      });
      return;
    }

    // Use spawn for better security and control
    const run = await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: "DotNetPrune:",
        cancellable: true,
      },
      async (progress, token) => {
          progress.report({ message: "Executing DotNet Prune analyzer..." });

          return new Promise<boolean>((resolve) => {
            const cfg = getConfig();
            
            // Build CLI arguments from configuration
            const cliArgs: string[] = [dllPath, chosenPath];
            
            if (cfg.analysis.includePublicSymbols === false) {
              cliArgs.push("--exclude-public");
            }
            if (cfg.analysis.includeInternalSymbols === false) {
              cliArgs.push("--exclude-internal");
            }
            if (cfg.analysis.excludeGeneratedCode === false) {
              cliArgs.push("--include-generated");
            }
            if (cfg.analysis.mode === "strict") {
              cliArgs.push("--strict");
            }
            if (cfg.maxFindings > 0) {
              cliArgs.push("--max-findings", cfg.maxFindings.toString());
            }

            const child = spawn("dotnet", cliArgs, {
              cwd: getWorkspaceRootPath(),
              stdio: ["ignore", "pipe", "pipe"],
            });

            let stdout = "";
            let stderr = "";
            let cancelled = false;

            // Explicit 5-minute timeout to ensure hung processes are killed
            const TIMEOUT_MS = 300000;
            const timeoutHandle = setTimeout(() => {
              if (!cancelled) {
                cancelled = true;
                child.kill();
                const msg =
                  "Analysis timed out. Try running on a smaller project or increasing the timeout.";
                this.appendToOutput(msg, "error");
                vscode.window
                  .showErrorMessage(`DotNetPrune: ${msg}`, "Open Output")
                  .then((choice) => {
                    if (choice === "Open Output") {
                      outputChannel?.show(true);
                    }
                  });
                resolve(false);
              }
            }, TIMEOUT_MS);

            const cleanup = () => clearTimeout(timeoutHandle);

            // Handle cancellation
            token.onCancellationRequested(() => {
              cancelled = true;
              cleanup();
              child.kill();
              resolve(false);
            });

            child.stdout.on("data", (data: Buffer) => {
              stdout += data.toString();
            });

            child.stderr.on("data", (data: Buffer) => {
              stderr += data.toString();
            });

            child.on("close", async (_code: number) => {
              // No-op after cancellation to avoid double-resolve and spurious errors
              if (cancelled) return;
              cleanup();
              try {
                // Parse JSON response from stdout
                const trimmedStdout = stdout.trim();
                if (!trimmedStdout) {
                  throw new Error(
                    "Analyzer produced no output. Ensure a valid .sln or .csproj was selected."
                  );
                }

                // Parse the structured response
                interface AnalysisResponse {
                  version: string;
                  success: boolean;
                  findings: any[];
                  error?: { code: string; message: string; details?: unknown };
                  metadata?: { analyzedAt: string; durationMs: number; filesScanned: number; symbolsAnalyzed: number };
                }

                let response: AnalysisResponse;
                try {
                  response = JSON.parse(trimmedStdout);
                } catch (parseError) {
                  // Fallback: try to extract JSON object if wrapped in other text
                  const jsonMatch = trimmedStdout.match(/(\{[\s\S]*\})/);
                  if (!jsonMatch) {
                    this.appendToOutput(`Raw stdout: ${stdout.substring(0, 2000)}`, "debug");
                    throw new Error(
                      "Invalid output from analyzer. Expected JSON object. See Output panel for details."
                    );
                  }
                  response = JSON.parse(jsonMatch[1]);
                }

                // Check for structured error - only treat as error if there's an actual error message
                if (response.error && response.error.message) {
                  const errorMsg = response.error.message;
                  this.appendToOutput(`Analyzer error: ${errorMsg}`, "error");
                  throw new Error(errorMsg);
                }

                // If success is false with no error, it's just an empty result (no findings)
                if (response.success === false) {
                  this.appendToOutput("Analysis complete: no unused code found.", "info");
                  await this.loadFindingsFromJson([]);
                  resolve(true);
                  return;
                }

                // Log metadata if present
                if (response.metadata) {
                  this.appendToOutput(
                    `Analysis complete: ${response.metadata.filesScanned} files, ${response.metadata.symbolsAnalyzed} symbols in ${response.metadata.durationMs}ms`,
                    "info"
                  );
                }

                const findings = response.findings;

                // Handle case where findings is undefined/null
                if (!findings) {
                  this.appendToOutput("Analysis complete: no unused code found.", "info");
                  await this.loadFindingsFromJson([]);
                  resolve(true);
                  return;
                }

                // Validate findings structure
                if (!Array.isArray(findings)) {
                  this.appendToOutput(`Raw stdout: ${stdout.substring(0, 2000)}`, "debug");
                  throw new Error(
                    "Analyzer output is not a valid findings array. See Output panel for details."
                  );
                }

                // Apply maxFindings limit from settings
                const cfg = getConfig();
                const limited = findings.slice(0, cfg.maxFindings);
                if (findings.length > cfg.maxFindings) {
                  this.appendToOutput(
                    `Warning: ${findings.length} findings found; displaying first ${cfg.maxFindings} (dotnetprune.maxFindings).`,
                    "warn"
                  );
                }

                await this.loadFindingsFromJson(limited);
                resolve(true);
              } catch (error: any) {
                this.appendToOutput(error.message, "error");
                vscode.window.showErrorMessage(
                  `DotNetPrune: ${error.message}`,
                  "Open Output"
                ).then((choice) => {
                  if (choice === "Open Output") {
                    outputChannel?.show(true);
                  }
                });
                resolve(false);
              }
            });

            child.on("error", (error: NodeJS.ErrnoException) => {
              if (cancelled) return;
              cleanup();
              cancelled = true;
              let userMessage: string;
              if (error.code === "ENOENT") {
                userMessage =
                  ".NET runtime not found. Install the .NET SDK (https://dot.net) and ensure 'dotnet' is on your PATH.";
              } else if (error.code === "ETIMEDOUT") {
                userMessage =
                  "Analysis timed out. Try running on a smaller project or increasing the timeout.";
              } else {
                userMessage = `Failed to start analyzer: ${error.message}`;
              }
              this.appendToOutput(`Spawn error: ${error.message} (code: ${error.code})`, "error");
              this.appendToOutput(userMessage, "error");
              vscode.window.showErrorMessage(`DotNetPrune: ${userMessage}`, "Open Output").then((choice) => {
                if (choice === "Open Output") {
                  outputChannel?.show(true);
                }
              });
              resolve(false);
            });
          });
        }
      );

      if (!run) return;

      // Update file hash cache after successful analysis
      if (this.fileCache) {
        const csprojFiles = await vscode.workspace.findFiles(
          "**/*.csproj",
          "**/{bin,debug,obj,release}/**",
          100
        );
        const filePaths = csprojFiles.map(f => f.fsPath);
        const analyzerVersion = "1.0.0"; // Could be read from extension version
        await this.fileCache.updateCache(filePaths, analyzerVersion);
        
        if (!silent) {
          const cacheSize = this.fileCache.getCacheSize();
          this.appendToOutput(`Cache updated: ${filePaths.length} files, ${(cacheSize / 1024).toFixed(1)}KB`, "debug");
        }
      }

      this._onDidChangeTreeData.fire(undefined);
      vscode.window.showInformationMessage(
        "DotNetPrune: Analysis completed."
      );

      // Open the DotNetPrune view to show the findings
      vscode.commands.executeCommand("workbench.view.dotnetprune-views");
    } finally {
      this.isAnalysisRunning = false;
    }
  }

  private getDllPath(): string {
    try {
      const cfg = getConfig();
      if (cfg.analyzerPath && cfg.analyzerPath.trim().length > 0) {
        return cfg.analyzerPath.trim();
      }
      const extensionPath = this.context.extensionPath;
      return path.join(extensionPath, "dist", "FindUnused", "FindUnused.dll");  
    } catch (error) {
      throw error;
    }
  }

  private isValidFilePath(filePath: string): boolean {
    try {
      // Basic validation: ensure it's an absolute path and exists
      if (!path.isAbsolute(filePath)) {
        return false;
      }

      // Check if file exists
      if (!fs.existsSync(filePath)) {
        return false;
      }

      // Ensure it's within the workspace
      const workspaceRoot = getWorkspaceRootPath();
      const relativePath = path.relative(workspaceRoot, filePath);
      if (relativePath.startsWith("..") || path.isAbsolute(relativePath)) {
        return false; // Path is outside workspace
      }

      // Check file extension
      const ext = path.extname(filePath).toLowerCase();
      return [".sln", ".slnx", ".csproj"].includes(ext);
    } catch (error) {
      return false;
    }
  }

  private extractProjectNameFromPath(filePath: string): string {
    try {
      const relativePath = path.relative(getWorkspaceRootPath(), filePath);
      const parts = relativePath.split(path.sep);

      // If the file is in a Models, Services, etc. subfolder, look for the actual project folder
      if (parts.length > 1) {
        // Check for common project folder patterns
        for (let i = 0; i < parts.length - 1; i++) {
          const part = parts[i];

          const skipDirectories = ['src', 'lib', 'test', 'tests', 'assets', 'resources', 'common', 'models', 'services', 'controllers'];
          // Skip common non-project directories
          if (skipDirectories.includes(part.toLowerCase())) {
            continue;
          }

          // This might be the actual project folder
          if (part && part !== '' && !part.includes('.')) {
            // Verify this folder contains code files or is a project folder
            const projectFolderPath = path.join(getWorkspaceRootPath(), parts.slice(0, i + 1).join(path.sep));

            if (fs.existsSync(projectFolderPath)) {
              try {
                const files = fs.readdirSync(projectFolderPath);
                const hasCsFiles = files.some(f => f.endsWith('.cs'));
                const hasCsproj = files.some(f => f.endsWith('.csproj'));

                if (hasCsFiles || hasCsproj) {
                  return part;
                }
              } catch (e) {
                // Continue if we can't read the directory
              }
            }
          }
        }
      }

      // Fallback: look for .csproj files in the directory structure
      const projectInfo = this.findProjectForFile(filePath);
      if (projectInfo) {
        return projectInfo;
      }

      // Ultimate fallback: use the first directory
      const topLevelDir = parts[0];
      if (topLevelDir && topLevelDir !== '') {
        return topLevelDir;
      }

      return "Project";
    } catch (error) {
      return "Project";
    }
  }

  private findProjectForFile(filePath: string): string | null {
    try {
      const fileDir = path.dirname(filePath);
      const workspaceRoot = getWorkspaceRootPath();

      // Walk up the directory tree looking for .csproj files
      let currentDir = fileDir;
      while (currentDir !== workspaceRoot && currentDir !== path.dirname(currentDir)) {
        try {
          const files = fs.readdirSync(currentDir);
          const csprojFiles = files.filter(f => f.endsWith('.csproj'));

          if (csprojFiles.length > 0) {
            // Use the first .csproj file found
            const projectName = path.basename(csprojFiles[0], '.csproj');
            return projectName;
          }
        } catch (e) {
          // Continue if we can't read the directory
        }

        currentDir = path.dirname(currentDir);
      }
    } catch (error) {
      // Ignore errors
    }

    return null;
  }

  private categorizeByFilePath(filePath: string): string {
    try {
      const relativePath = path.relative(getWorkspaceRootPath(), filePath);
      const parts = relativePath.split(path.sep);

      // Look for solution files in parent directories
      for (let i = 0; i < parts.length; i++) {
        const currentPath = parts.slice(0, i + 1).join(path.sep);
        const dirPath = path.join(getWorkspaceRootPath(), currentPath);

        if (fs.existsSync(dirPath)) {
          const files = fs.readdirSync(dirPath);
          const hasSlnFile = files.some(f => f.toLowerCase().endsWith('.sln') || f.toLowerCase().endsWith('.slnx'));

          if (hasSlnFile) {
            const solutionName = path.basename(currentPath);
            return solutionName;
          }
        }
      }

      // If no solution found, use the workspace folder name
      return path.basename(getWorkspaceRootPath());
    } catch (error) {
      return path.basename(getWorkspaceRootPath());
    }
  }

  private async loadFindingsFromJson(findingsJson: any[]): Promise<void> {
    if (!Array.isArray(findingsJson)) {
      throw new Error("Findings JSON must be an array.");
    }

    // Discover all solution files and project mappings in workspace
    await this.discoverSolutionsAndProjects();

    // Map to internal Finding type and normalize paths, filter for .NET files only
    const mapped: Finding[] = findingsJson
      .map((p: any) => {
        const filePath = p.FilePath ?? p.filePath ?? "";
        const resolved = path.isAbsolute(filePath)
          ? filePath
          : path.join(getWorkspaceRootPath(), filePath);

        // Extract project name from file path if not provided
        let projectName = p.Project ?? p.project ?? "";
        if (!projectName || projectName === "") {
          projectName = this.pathResolver.getProjectNameFromPath(resolved);
        } else {
          // If project name contains path separators, extract just the project name
          if (projectName.includes(path.sep)) {
            projectName = path.basename(projectName);
            // Clean up any trailing unwanted characters (like ))
            projectName = projectName.replace(/\)$/, '');
            // Remove .csproj extension if present
            if (projectName.endsWith('.csproj')) {
              projectName = projectName.slice(0, -7);
            }
          }
        }

        // Determine which solution this project belongs to
        const solution = this.pathResolver.getSolutionForFile(resolved) ?? undefined;

        return {
          Project: projectName,
          Solution: solution,
          FilePath: resolved,
          FilePathDisplay: p.FilePathDisplay ?? p.filePathDisplay ?? "",
          DisplayName: p.DisplayName ?? p.displayName ?? "",
          ProjectFilePath: p.ProjectFilePath ?? p.projectFilePath ?? "",
          Line: typeof p.Line === "number" ? p.Line : p.line ?? 1,
          SymbolKind: p.SymbolKind ?? p.symbolKind ?? "",
          ContainingType: p.ContainingType ?? p.containingType ?? "",
          SymbolName: p.SymbolName ?? p.symbolName ?? "",
          Accessibility: p.Accessibility ?? p.accessibility ?? "",
          Remarks: p.Remarks ?? p.remarks ?? "",
          confidence:
            typeof p.confidence === "number" ? p.confidence : undefined,
          severity: p.severity,
          Icon: p.Icon ?? p.icon ?? "",
        };
      })
      .filter((finding: Finding) => {
        // Only include findings from .NET-related files
        const ext = path.extname(finding.FilePath).toLowerCase();
        const dotNetFiles = [".cs", ".sln", ".slnx", ".csproj"];
        return dotNetFiles.includes(ext);
      })
      .filter((finding: Finding) => {
        // Filter by confidence level if configured
        const level = getConfidenceLevel();
        if (level === "all") return true;
        
        const confidence = finding.confidence ?? 100;
        
        if (level === "high") {
          return confidence >= 80;
        } else if (level === "medium") {
          return confidence >= 50 && confidence < 80;
        } else if (level === "low") {
          return confidence < 50;
        }
        return true;
      });

    // Store all findings
    this.allFindings = mapped;
    
    // Apply filters to get current findings
    this.applyFilters();
  }

  private async discoverSolutionsAndProjects(): Promise<void> {
    this.solutionFiles.clear();
    this.projectToSolutionMap.clear();

    // Exclude build folders when discovering solutions too
    const excludedFolders = "**/{bin,debug,obj,release,nuget,bin/**,debug/**,obj/**,release/**,nuget/**}/**";

    const slnxFiles = await vscode.workspace.findFiles(
      "**/*.slnx",
      `${excludedFolders},**/node_modules/**`,
      10
    );
    const slnFiles = await vscode.workspace.findFiles(
      "**/*.sln",
      `${excludedFolders},**/node_modules/**`,
      10
    );

    const allSolutions = [...slnxFiles, ...slnFiles];

    for (const solutionFile of allSolutions) {
      const solutionName = path.basename(
        solutionFile.fsPath,
        path.extname(solutionFile.fsPath)
      );
      this.solutionFiles.set(solutionName, solutionFile.fsPath);

      // Discover projects associated with this solution
      await this.discoverProjectsForSolution(solutionFile.fsPath, solutionName);
    }

    // Also discover standalone projects (projects not in solutions)
    await this.discoverStandaloneProjects();
  }

  private async discoverStandaloneProjects(): Promise<void> {
    try {
      const excludedFolders = "**/{bin,debug,obj,release,nuget,bin/**,debug/**,obj/**,release/**,nuget/**}/**";

      const csprojFiles = await vscode.workspace.findFiles(
        "**/*.csproj",
        `${excludedFolders},**/node_modules/**`,
        100
      );

      for (const csprojFile of csprojFiles) {
        const projectName = path.basename(csprojFile.fsPath, '.csproj');
        const projectDir = path.dirname(csprojFile.fsPath);

        // Check if this project is already associated with a solution
        let alreadyAssociated = false;
        for (const [_, solutionName] of this.solutionFiles) {
          if (projectDir.startsWith(path.dirname(solutionName))) {
            alreadyAssociated = true;
            break;
          }
        }

        if (!alreadyAssociated) {
          this.projectToSolutionMap.set(projectName, path.basename(getWorkspaceRootPath()));
        }
      }
    } catch (error) {
      this.appendToOutput(`Warning: Could not discover standalone projects: ${error}`, "warn");
    }
  }

  private async discoverProjectsForSolution(solutionPath: string, solutionName: string): Promise<void> {
    try {
      const solutionDir = path.dirname(solutionPath);
      const csprojFiles = await vscode.workspace.findFiles(
        `${path.relative(getWorkspaceRootPath(), solutionDir)}/**/*.csproj`,
        "**/{bin,debug,obj,release,nuget}/**",
        100
      );

      for (const csprojFile of csprojFiles) {
        const projectName = path.basename(csprojFile.fsPath, '.csproj');
        this.projectToSolutionMap.set(projectName, solutionName);

        // Associate the project directory as well
        const projectDir = path.dirname(csprojFile.fsPath);
        const relativeProjectDir = path.relative(solutionDir, projectDir);
        const dirName = path.basename(projectDir);

        // If the project is in a subdirectory, associate that too
        if (dirName && dirName !== solutionName) {
          this.projectToSolutionMap.set(dirName, solutionName);
        }

        // Also try to find all directories in the project path that might be referenced
        if (relativeProjectDir && relativeProjectDir !== '.') {
          const pathParts = relativeProjectDir.split(path.sep);
          for (let i = 0; i < pathParts.length; i++) {
            const partialPath = pathParts.slice(0, i + 1).join(path.sep);
            const fullPath = path.join(solutionDir, partialPath);
            const partialDirName = path.basename(fullPath);

            if (partialDirName && partialDirName !== solutionName) {
              this.projectToSolutionMap.set(partialDirName, solutionName);
            }
          }
        }
      }

      this.appendToOutput(`Discovered ${csprojFiles.length} projects for solution ${solutionName}`, "debug");
    } catch (error) {
      // Ignore errors in project discovery
      this.appendToOutput(`Warning: Could not discover projects for solution ${solutionName}: ${error}`, "warn");
    }
  }

  private findSolutionForProject(projectName: string, filePath?: string): string | undefined {
    // First check our project-to-solution mapping
    if (this.projectToSolutionMap.has(projectName)) {
      return this.projectToSolutionMap.get(projectName);
    }

    // Enhanced logic for namespace-based projects (e.g., "FlowCore.Models")
    if (projectName.includes('.')) {
      const namespaceParts = projectName.split('.');
      // Try each part of the namespace to find a matching project
      for (let i = namespaceParts.length - 1; i >= 0; i--) {
        const partialProject = namespaceParts.slice(0, i + 1).join('.');
        if (this.projectToSolutionMap.has(partialProject)) {
          return this.projectToSolutionMap.get(partialProject);
        }

        // Also try just the last part (e.g., "Models")
        if (i === namespaceParts.length - 1 && this.projectToSolutionMap.has(namespaceParts[i])) {
          return this.projectToSolutionMap.get(namespaceParts[i]);
        }
      }
    }

    // Try fuzzy matching with known solutions
    for (const [solutionName] of this.solutionFiles) {
      if (
        projectName.toLowerCase().includes(solutionName.toLowerCase()) ||
        solutionName.toLowerCase().includes(projectName.toLowerCase()) ||
        this.hasFuzzyMatch(projectName, solutionName)
      ) {
        return solutionName;
      }
    }

    // Enhanced path-based solution finding
    if (filePath) {
      const pathBasedSolution = this.findSolutionByFilePath(filePath);
      if (pathBasedSolution) {
        return pathBasedSolution;
      }
    }

    return undefined;
  }

  private findSolutionByFilePath(filePath: string): string | null {
    try {
      const fileDir = path.dirname(filePath);
      const workspaceRoot = getWorkspaceRootPath();

      // Walk up the directory tree looking for solution files
      let currentDir = fileDir;
      while (currentDir !== workspaceRoot && currentDir !== path.dirname(currentDir)) {
        try {
          const files = fs.readdirSync(currentDir);
          const solutionFiles = files.filter(f =>
            f.toLowerCase().endsWith('.sln') || f.toLowerCase().endsWith('.slnx')
          );

          if (solutionFiles.length > 0) {
            const solutionName = path.basename(solutionFiles[0], path.extname(solutionFiles[0]));
            return solutionName;
          }
        } catch (e) {
          // Continue if we can't read the directory
        }

        currentDir = path.dirname(currentDir);
      }
    } catch (error) {
      // Ignore errors
    }

    return null;
  }

  private hasFuzzyMatch(projectName: string, solutionName: string): boolean {
    const projectWords = projectName.toLowerCase().split(/[\s\-_\.]/);
    const solutionWords = solutionName.toLowerCase().split(/[\s\-_\.]/);

    for (const pWord of projectWords) {
      for (const sWord of solutionWords) {
        if (pWord.length > 2 && sWord.length > 2 &&
          (pWord.includes(sWord) || sWord.includes(pWord))) {
          return true;
        }
      }
    }
    return false;
  }

  async openFinding(f: Finding) {
    if (!f || !f.FilePath) {
      vscode.window.showWarningMessage(
        "DotNetPrune: finding has no file path."
      );
      return;
    }
    try {
      const doc = await vscode.workspace.openTextDocument(f.FilePath);
      const editor = await vscode.window.showTextDocument(doc, {
        preview: false,
      });
      const line = Math.max(0, f.Line > 0 ? f.Line - 1 : 0);
      const pos = new vscode.Position(line, 0);
      editor.revealRange(
        new vscode.Range(pos, pos),
        vscode.TextEditorRevealType.InCenter
      );
      // optionally set selection to the line
      editor.selection = new vscode.Selection(pos, pos);
    } catch (err: any) {
      vscode.window.showErrorMessage(
        `DotNetPrune: failed to open file ${f.FilePath}: ${err.message || err}`
      );
    }
}

  async deleteFindings(items: FindingTreeItem[]): Promise<void> {
    if (!items || items.length === 0) {
      vscode.window.showWarningMessage("DotNetPrune: No findings selected for deletion.");
      return;
    }

    const findings = items.map(item => item.finding);
    const uniqueFiles = [...new Set(findings.map(f => f.FilePath))];

    const confirmMsg = items.length === 1
      ? `Delete unused symbol "${findings[0].SymbolName}"?`
      : `Delete ${items.length} unused symbols across ${uniqueFiles.length} files?`;

    const choice = await vscode.window.showQuickPick([
      { label: "Delete", id: "delete" },
      { label: "Cancel", id: "cancel" }
    ], { placeHolder: confirmMsg });

    if (!choice || choice.id === "cancel") {
      return;
    }

    const errors: string[] = [];
    const deleted: string[] = [];

    for (const finding of findings) {
      try {
        if (!finding.FilePath || !fs.existsSync(finding.FilePath)) {
          errors.push(`File not found: ${finding.FilePath}`);
          continue;
        }

        const content = fs.readFileSync(finding.FilePath, 'utf-8');
        const lines = content.split('\n');

        if (finding.Line < 1 || finding.Line > lines.length) {
          errors.push(`Invalid line ${finding.Line} in ${finding.FilePath}`);
          continue;
        }

        const lineIndex = finding.Line - 1;
        const line = lines[lineIndex];

        if (line.trim().length > 0) {
          lines[lineIndex] = '';
          fs.writeFileSync(finding.FilePath, lines.join('\n'), 'utf-8');
          deleted.push(finding.SymbolName);
        }
      } catch (err: any) {
        errors.push(`Failed to delete ${finding.SymbolName}: ${err.message}`);
      }
    }

    if (deleted.length > 0) {
      this.findings = this.findings.filter(f => !deleted.includes(f.SymbolName));
      this._onDidChangeTreeData.fire(undefined);
      vscode.window.showInformationMessage(`DotNetPrune: Deleted ${deleted.length} unused symbol(s).`);
    }

    if (errors.length > 0) {
      const errorMsg = `Errors during deletion: ${errors.join('; ')}`;
      vscode.window.showWarningMessage(errorMsg);
      this.appendToOutput(errorMsg, "warn");
    }
  }

  async generateCleanupScript(items: FindingTreeItem[]): Promise<void> {
    if (!items || items.length === 0) {
      vscode.window.showWarningMessage("DotNetPrune: No findings selected for cleanup script.");
      return;
    }

    const findings = items.map(item => item.finding);
    const uniqueFiles = [...new Set(findings.map(f => f.FilePath))];

    const scriptLines: string[] = [];
    scriptLines.push('# DotNetPrune Cleanup Script');
    scriptLines.push(`# Generated: ${new Date().toISOString()}`);
    scriptLines.push(`# Items: ${items.length}`);
    scriptLines.push('');

    const fileGroups = new Map<string, Finding[]>();
    for (const f of findings) {
      const existing = fileGroups.get(f.FilePath) || [];
      existing.push(f);
      fileGroups.set(f.FilePath, existing);
    }

    for (const [filePath, fileFindings] of fileGroups) {
      const relativePath = path.relative(getWorkspaceRootPath(), filePath);
      scriptLines.push(`# File: ${relativePath}`);
      scriptLines.push(`# Symbols to remove: ${fileFindings.length}`);

      for (const f of fileFindings) {
        const action = f.SymbolKind.toLowerCase().includes('method') || f.SymbolKind.toLowerCase().includes('function')
          ? `// Consider removing method: ${f.SymbolName}`
          : `// Consider removing: ${f.SymbolName} (${f.SymbolKind})`;
        scriptLines.push(action);
      }
      scriptLines.push('');
    }

    scriptLines.push('# Review each file and remove unused symbols manually.');
    scriptLines.push('# The analyzer can be run again after cleanup to verify.');

    const scriptContent = scriptLines.join('\n');

    const doc = await vscode.workspace.openTextDocument({
      content: scriptContent,
      language: 'plaintext'
    });
    await vscode.window.showTextDocument(doc, { preview: false });

    vscode.window.showInformationMessage(
      `DotNetPrune: Generated cleanup script for ${items.length} item(s).`
    );
  }

  // Filter methods
  async filterBySymbolKind(): Promise<void> {
    const allKinds = new Set(this.allFindings.map((f) => f.SymbolKind));
    const picks = Array.from(allKinds).map((kind) => ({
      label: kind,
      picked: false,
    }));

    const selected = await vscode.window.showQuickPick(picks, {
      canPickMany: true,
      placeHolder: "Select symbol kinds to show",
    });

    if (selected) {
      this.filter.setSymbolKindFilter(selected.map((s) => s.label));
      this.applyFilters();
    }
  }

  async filterByConfidence(): Promise<void> {
    const input = await vscode.window.showInputBox({
      prompt: "Enter minimum confidence (0-100)",
      placeHolder: "50",
      validateInput: (value) => {
        const num = Number.parseInt(value);
        if (Number.isNaN(num) || num < 0 || num > 100) {
          return "Please enter a number between 0 and 100";
        }
        return null;
      },
    });

    if (input !== undefined) {
      this.filter.setConfidenceFilter(Number.parseInt(input));
      this.applyFilters();
    }
  }

  async filterByProject(): Promise<void> {
    const allProjects = new Set(this.allFindings.map((f) => f.Project));
    const picks = Array.from(allProjects).map((project) => ({
      label: project,
      picked: false,
    }));

    const selected = await vscode.window.showQuickPick(picks, {
      canPickMany: true,
      placeHolder: "Select projects to show",
    });

    if (selected) {
      this.filter.setProjectFilter(selected.map((s) => s.label));
      this.applyFilters();
    }
  }

  async searchFindings(): Promise<void> {
    const useRegex = await vscode.window.showQuickPick(
      [
        { label: "Normal text search", description: "Case-insensitive text match", value: "text" },
        { label: "Regex search", description: "Regular expression match", value: "regex" },
      ],
      { placeHolder: "Select search mode" }
    );
    
    if (!useRegex) {
      return;
    }

    const prompt = useRegex.value === "regex"
      ? "Search findings using regex pattern"
      : "Search findings (symbol name, type, file, etc.)";
    const placeHolder = useRegex.value === "regex"
      ? "Enter regex pattern..."
      : "Enter search text...";

    const input = await vscode.window.showInputBox({
      prompt,
      placeHolder,
    });

    if (input !== undefined && input !== "") {
      if (useRegex.value === "regex") {
        this.filter.setSearchRegex(input);
      } else {
        this.filter.setSearchText(input);
      }
      this.applyFilters();
      const count = this.findings.length;
      vscode.window.setStatusBarMessage(`DotNetPrune: ${count} of ${this.allFindings.length} items`, 3000);
    }
  }

  clearFilters(): void {
    this.filter.clearAll();
    this.showOnlyDeletable = false;
    this.applyFilters();
    vscode.window.showInformationMessage("DotNetPrune: All filters cleared");
  }

  private applyFilters(): void {
    this.findings = this.allFindings.filter((f) =>
      this.filter.matches(f) &&
      (!this.showOnlyDeletable || (f.confidence !== undefined && f.confidence >= 80))
    );
    
    this.rebuildGroupedStructure();
    
    this._onDidChangeTreeData.fire(undefined);
    
    this.updateIntegrations();
  }

  private rebuildGroupedStructure(): void {
    this.groupedBySolution.clear();

    for (const f of this.findings) {
      const solutionName = f.Solution || this.pathResolver.getSolutionNameFromPath(f.FilePath);
      const projectName = f.Project || this.pathResolver.getProjectNameFromPath(f.FilePath);

      if (!this.groupedBySolution.has(solutionName)) {
        this.groupedBySolution.set(solutionName, new Map());
      }

      const projectsMap = this.groupedBySolution.get(solutionName)!;
      if (!projectsMap.has(projectName)) {
        projectsMap.set(projectName, new Map());
      }

      const filesMap = projectsMap.get(projectName)!;

      if (this.groupByType && f.ContainingType) {
        const typeKey = f.ContainingType;
        if (!filesMap.has(typeKey)) filesMap.set(typeKey, []);
        filesMap.get(typeKey)!.push(f);
      } else {
        const fileKey = f.FilePath || "(generated)";
        if (!filesMap.has(fileKey)) filesMap.set(fileKey, []);
        filesMap.get(fileKey)!.push(f);
      }
    }
  }

  toggleGroupByType(): void {
    this.groupByType = !this.groupByType;
    this.rebuildGroupedStructure();
    this._onDidChangeTreeData.fire(undefined);
    vscode.window.showInformationMessage(
      `DotNetPrune: Group by type ${this.groupByType ? "enabled" : "disabled"}`
    );
  }

  toggleViewMode(): void {
    this.viewMode = this.viewMode === "compact" ? "full" : "compact";
    this.rebuildGroupedStructure();
    this._onDidChangeTreeData.fire(undefined);
    vscode.window.showInformationMessage(
      `DotNetPrune: View mode set to ${this.viewMode === "compact" ? "Compact (findings-only)" : "Full (all files)"}`
    );
  }

  toggleShowDeletable(): void {
    this.showOnlyDeletable = !this.showOnlyDeletable;
    this.applyFilters();
    const count = this.findings.length;
    vscode.window.showInformationMessage(
      `DotNetPrune: ${this.showOnlyDeletable ? "Showing only deletable (≥80%)" : "Showing all findings"} — ${count} items`
    );
  }

  private updateIntegrations(): void {
    const config = getConfig();
    
    if (this.diagnosticProvider) {
      if (config.integration.enableProblemsPanel) {
        this.diagnosticProvider.updateDiagnostics(this.findings);
      } else {
        this.diagnosticProvider.clear();
      }
    }
    
    if (this.codeActionsProvider) {
      if (config.integration.enableCodeActions) {
        this.codeActionsProvider.updateFindings(this.findings);
      } else {
        this.codeActionsProvider.updateFindings([]);
      }
    }
    
    if (this.decorator) {
      if (config.ui.enableInlineHighlighting) {
        this.decorator.updateFindings(this.findings);
      } else {
        this.decorator.clear();
      }
    }
  }

  async ignoreFinding(finding: Finding): Promise<void> {
    this.filter.addIgnored(finding);
    await this.context.workspaceState.update(
      "ignoredFindings",
      Array.from(this.filter.getIgnoredFindings())
    );
    
    this.applyFilters();
    
    vscode.window.showInformationMessage("DotNetPrune: Finding ignored");
  }

  async deleteFinding(finding: Finding): Promise<void> {
    const confirm = await vscode.window.showWarningMessage(
      `Delete unused ${finding.SymbolKind.toLowerCase()} "${finding.SymbolName}"?`,
      { modal: true },
      "Delete",
      "Cancel"
    );

    if (confirm !== "Delete") return;

    try {
      const doc = await vscode.workspace.openTextDocument(finding.FilePath);
      await vscode.window.showTextDocument(doc);
      
      const line = Math.max(0, finding.Line - 1);
      
      const edit = new vscode.WorkspaceEdit();
      edit.delete(doc.uri, new vscode.Range(line, 0, line + 1, 0));
      
      await vscode.workspace.applyEdit(edit);
      await doc.save();
      
      await this.ignoreFinding(finding);
      
      vscode.window.showInformationMessage(
        `DotNetPrune: Deleted ${finding.SymbolKind.toLowerCase()} "${finding.SymbolName}"`
      );
    } catch (err: any) {
      vscode.window.showErrorMessage(
        `DotNetPrune: Failed to delete: ${err.message}`
      );
    }
  }

  showFindingDetails(finding: Finding): void {
    const panel = vscode.window.createWebviewPanel(
      "dotnetpruneFindingDetails",
      `Finding: ${finding.SymbolName}`,
      vscode.ViewColumn.Beside,
      {}
    );

    panel.webview.html = this.getFindingDetailsHtml(finding);
  }

  private escapeHtml(value: string): string {
    if (!value) return "";
    return value
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#039;");
  }

  private getFindingDetailsHtml(finding: Finding): string {
    const esc = (v: string | undefined) => this.escapeHtml(v ?? "");
    return `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <style>
    body { font-family: var(--vscode-font-family); padding: 20px; }
    h1 { color: var(--vscode-foreground); }
    .detail { margin: 10px 0; }
    .label { font-weight: bold; color: var(--vscode-descriptionForeground); }
    .value { color: var(--vscode-foreground); }
  </style>
</head>
<body>
  <h1>${esc(finding.SymbolKind)}: ${esc(finding.SymbolName)}</h1>
  <div class="detail">
    <span class="label">Type:</span>
    <span class="value">${esc(finding.ContainingType)}</span>
  </div>
  <div class="detail">
    <span class="label">File:</span>
    <span class="value">${esc(finding.FilePathDisplay || finding.FilePath)}</span>
  </div>
  <div class="detail">
    <span class="label">Line:</span>
    <span class="value">${finding.Line}</span>
  </div>
  <div class="detail">
    <span class="label">Accessibility:</span>
    <span class="value">${esc(finding.Accessibility)}</span>
  </div>
  ${finding.confidence !== undefined ? `
  <div class="detail">
    <span class="label">Confidence:</span>
    <span class="value">${finding.confidence}%</span>
  </div>
  ` : ''}
  ${finding.Remarks ? `
  <div class="detail">
    <span class="label">Details:</span>
    <span class="value">${esc(finding.Remarks)}</span>
  </div>
  ` : ""}
  ${finding.referenceCount !== undefined ? `
  <div class="detail">
    <span class="label">Reference Count:</span>
    <span class="value">${finding.referenceCount}</span>
  </div>
  ` : ""}
  ${finding.references && finding.references.length > 0 ? `
  <div class="detail">
    <span class="label">References:</span>
    <ul>
      ${finding.references.map(ref => `
        <li>
          <span class="value">${esc(ref.filePath)}:${ref.line}</span>
          <span class="label"> (${ref.type})</span>
          ${ref.context ? `<span class="value"> - ${esc(ref.context)}</span>` : ""}
        </li>
      `).join('')}
    </ul>
  </div>
  ` : ""}
</body>
</html>`;
  }

  private appendToOutput(text: string, level: "error" | "warn" | "info" | "debug" = "info") {
    const cfg = getConfig();
    const configuredLevel = LOG_LEVELS[cfg.logLevel] ?? LOG_LEVELS.info;
    const messageLevel = LOG_LEVELS[level];

    if (configuredLevel === 0 || messageLevel > configuredLevel) {
      return;
    }

    if (!outputChannel) {
      outputChannel = vscode.window.createOutputChannel("DotNetPrune");
    }
    outputChannel.appendLine(`[${level.toUpperCase()}] ${text}`);
    if (level === "error" || level === "warn") {
      outputChannel.show(true);
    }
  }

  getTreeItem(element: TreeItemBase): vscode.TreeItem {
    return element;
  }

  getChildren(element?: TreeItemBase): Thenable<TreeItemBase[]> {
    if (!element) {
      // top-level: solutions
      const items = Array.from(this.groupedBySolution.keys()).map(
        (solution) => {
          const item = new SolutionTreeItem(
            solution,
            vscode.TreeItemCollapsibleState.Collapsed
          );
          return item;
        }
      );
      // if no findings, show hint
      if (items.length === 0) {
        return Promise.resolve([
          new MessageTreeItem(
            "No findings. Run analysis to scan for unused code.",
            vscode.TreeItemCollapsibleState.None
          ),
        ]);
      }
      return Promise.resolve(items);
    }

    if (element instanceof SolutionTreeItem) {
      const solution = element.label as string;
      const projects = this.groupedBySolution.get(solution);
      if (!projects) return Promise.resolve([]);
      const projectItems: TreeItemBase[] = [];
      for (const [projectName] of projects) {
        const projectItem = new ProjectTreeItem(
          projectName,
          solution,
          vscode.TreeItemCollapsibleState.Collapsed
        );
        projectItems.push(projectItem);
      }
      return Promise.resolve(projectItems);
    }

    if (element instanceof ProjectTreeItem) {
      const solution = element.solutionName;
      const projectName = element.label as string;

      const solutionData = this.groupedBySolution.get(solution);
      if (!solutionData) return Promise.resolve([]);

      const files = solutionData.get(projectName);
      if (!files) return Promise.resolve([]);

      const fileItems: TreeItemBase[] = [];
      for (const [filePath, findings] of files) {
        const hasDeletable = findings.some(f => f.confidence !== undefined && f.confidence >= 80);
        const hasMedium = findings.some(f => f.confidence !== undefined && f.confidence >= 50 && f.confidence < 80);
        
        if (this.viewMode === "compact" && findings.length === 0) {
          continue;
        }
        
        const displayName = findings.length > 0 && findings[0].DisplayName
          ? findings[0].DisplayName
          : path.basename(filePath);
        const filePathDisplay = findings.length > 0 && findings[0].FilePathDisplay
          ? findings[0].FilePathDisplay
          : path.relative(getWorkspaceRootPath(), filePath);

        const fileItem = new FileTreeItem(
          displayName,
          filePath,
          solution,
          projectName,
          vscode.TreeItemCollapsibleState.Collapsed,
          hasDeletable,
          hasMedium
        );
        fileItem.tooltip = filePathDisplay;
        fileItems.push(fileItem);
      }
      fileItems.sort((a, b) => {
        const aFile = a as FileTreeItem;
        const bFile = b as FileTreeItem;
        if (aFile.hasDeletable && !bFile.hasDeletable) return -1;
        if (!aFile.hasDeletable && bFile.hasDeletable) return 1;
        return 0;
      });
      return Promise.resolve(fileItems);
    }

    if (element instanceof FileTreeItem) {
      const filePath = element.filePath;
      const solution = element.solutionName;
      const projectName = element.projectName;

      const solutionData = this.groupedBySolution.get(solution);
      if (!solutionData) return Promise.resolve([]);

      const filesMap = solutionData.get(projectName);
      if (!filesMap) return Promise.resolve([]);

      const findings = filesMap.get(filePath) || [];
      const items = findings.map((f) => {
        const label = `${f.SymbolKind}: ${f.SymbolName}`;
        const ti = new FindingTreeItem(
          label,
          f,
          vscode.TreeItemCollapsibleState.None
        );
        const projectInfo = f.ProjectFilePath ? `Project: ${path.basename(f.ProjectFilePath)}` : "";
        const fileInfo = f.FilePathDisplay ? `File: ${f.FilePathDisplay}` : "";
        ti.tooltip = `${f.ContainingType} — ${f.Remarks}\n${projectInfo}\n${fileInfo}`.trim();
        ti.description = `Ln ${f.Line} (${f.Accessibility})`;
        return ti;
      });
      return Promise.resolve(items);
    }

    return Promise.resolve([]);
  }
}

abstract class TreeItemBase extends vscode.TreeItem { }

class MessageTreeItem extends TreeItemBase {
  constructor(message: string, state: vscode.TreeItemCollapsibleState) {
    super(message, state);
    this.contextValue = "message";
    this.iconPath = new vscode.ThemeIcon("info");
  }
}

class SolutionTreeItem extends TreeItemBase {
  constructor(
    public readonly label: string,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(label, state);
    this.contextValue = "solution";
    this.iconPath = new vscode.ThemeIcon("root-folder");
  }
}

class ProjectTreeItem extends TreeItemBase {
  constructor(
    public readonly label: string,
    public readonly solutionName: string,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(label, state);
    this.contextValue = "project";
    this.iconPath = new vscode.ThemeIcon("project");
  }
}

class FileTreeItem extends TreeItemBase {
  public readonly hasDeletable: boolean = false;
  public readonly hasMedium: boolean = false;
  
  constructor(
    public readonly label: string,
    public readonly filePath: string,
    public readonly solutionName: string,
    public readonly projectName: string,
    state: vscode.TreeItemCollapsibleState,
    hasDeletable: boolean = false,
    hasMedium: boolean = false
  ) {
    super(label, state);
    this.contextValue = "file";
    this.hasDeletable = hasDeletable;
    this.hasMedium = hasMedium;
    
    if (hasDeletable) {
      this.iconPath = new vscode.ThemeIcon("file-code");
      this.label = `$(warning) ${label}`;
    } else if (hasMedium) {
      this.iconPath = new vscode.ThemeIcon("file-code");
      this.label = `$(circle-outline) ${label}`;
    } else {
      this.iconPath = path.extname(filePath).toLowerCase() === '.cs' ? new vscode.ThemeIcon("file-code") : new vscode.ThemeIcon("file");
    }
  }
}

class FindingTreeItem extends TreeItemBase {
  constructor(
    public readonly label: string,
    public readonly finding: Finding,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(label, state);
    this.contextValue = "finding";
    const confidence = finding.confidence;
    const isDeletable = confidence !== undefined && confidence >= 80;
    const isMedium = confidence !== undefined && confidence >= 50 && confidence < 80;
    
    if (finding.Icon) {
      this.iconPath = new vscode.ThemeIcon(finding.Icon);
    } else if (isDeletable) {
      this.iconPath = new vscode.ThemeIcon("trash");
      this.label = `$(trash) ${label}`;
    } else if (isMedium) {
      this.iconPath = new vscode.ThemeIcon("warning");
      this.label = `$(warning) ${label}`;
    } else {
      this.iconPath = getIconForSymbolKind(finding.SymbolKind);
    }
    
    if (confidence !== undefined) {
      this.description = `Ln ${finding.Line} (${confidence}%)`;
      if (isDeletable) {
        this.description += " [Deletable]";
      }
    } else {
      this.description = `Ln ${finding.Line}`;
    }
  }
}

class MetricsTreeProvider implements vscode.TreeDataProvider<vscode.TreeItem> {
  private _onDidChangeTreeData: vscode.EventEmitter<vscode.TreeItem | undefined> =
    new vscode.EventEmitter<vscode.TreeItem | undefined>();
  readonly onDidChangeTreeData: vscode.Event<vscode.TreeItem | undefined> =
    this._onDidChangeTreeData.event;

  constructor(
    private context: vscode.ExtensionContext,
    private findingsProvider: UnusedTreeProvider
  ) {}

  refresh(): void {
    this._onDidChangeTreeData.fire(undefined);
  }

  getTreeItem(element: vscode.TreeItem): vscode.TreeItem {
    return element;
  }

  getChildren(element?: vscode.TreeItem): Thenable<vscode.TreeItem[]> {
    const metrics = this.calculateMetrics();

    if (metrics.totalUnused === 0) {
      return Promise.resolve([
        new MetricsMessageItem(
          "No metrics available. Run analysis to see metrics.",
          vscode.TreeItemCollapsibleState.None
        )
      ]);
    }

    if (!element) {
      const items: vscode.TreeItem[] = [];

      const overviewItem = new MetricsCategoryItem(
        `Overview (${metrics.totalUnused} unused items, ${metrics.filesAffected} files)`,
        vscode.TreeItemCollapsibleState.Collapsed,
        "symbol-property"
      );
      overviewItem.description = `${metrics.filesAffected} files`;
      items.push(overviewItem);

      const symbolKindItem = new MetricsCategoryItem(
        "By Symbol Kind",
        vscode.TreeItemCollapsibleState.Collapsed,
        "symbol-method"
      );
      symbolKindItem.description = `${Object.keys(metrics.bySymbolKind).length} types`;
      items.push(symbolKindItem);

      const projectItem = new MetricsCategoryItem(
        "By Project",
        vscode.TreeItemCollapsibleState.Collapsed,
        "project"
      );
      projectItem.description = `${Object.keys(metrics.byProject).length} projects`;
      items.push(projectItem);

      if (metrics.lastAnalyzed) {
        const timestampItem = new vscode.TreeItem(`Last analyzed: ${metrics.lastAnalyzed}`);
        timestampItem.contextValue = "timestamp";
        items.push(timestampItem);
      }

      return Promise.resolve(items);
    }

    if (element instanceof MetricsCategoryItem) {
      const label = element.label as string;
      
      if (label.includes("Overview")) {
        return Promise.resolve([
          this.createMetricItem("Total unused items", `${metrics.totalUnused}`),
          this.createMetricItem("Files affected", `${metrics.filesAffected}`)
        ]);
      }
      
      if (label.includes("Symbol Kind")) {
        const sortedKinds = Object.entries(metrics.bySymbolKind).sort((a, b) => b[1] - a[1]);
        return Promise.resolve(
          sortedKinds.map(([kind, count]) => 
            this.createMetricItem(kind, `${count}`)
          )
        );
      }
      
      if (label.includes("Project")) {
        const sortedProjects = Object.entries(metrics.byProject).sort((a, b) => b[1] - a[1]);
        return Promise.resolve(
          sortedProjects.map(([project, count]) => 
            this.createMetricItem(project, `${count}`)
          )
        );
      }
    }

    return Promise.resolve([]);
  }

  private createMetricItem(label: string, value: string): vscode.TreeItem {
    const item = new vscode.TreeItem(`${label}: ${value}`);
    item.contextValue = "metric-item";
    item.iconPath = new vscode.ThemeIcon("number");
    return item;
  }

  getParent(_element: vscode.TreeItem): Thenable<vscode.TreeItem | undefined> {
    return Promise.resolve(undefined);
  }

  private calculateMetrics(): MetricsData {
    const findings = this.findingsProvider.getAllFindings();
    const bySymbolKind: Record<string, number> = {};
    const byProject: Record<string, number> = {};
    const uniqueFiles = new Set<string>();

    for (const finding of findings) {
      if (finding.FilePath) {
        uniqueFiles.add(finding.FilePath);
      }

      const symbolKind = finding.SymbolKind || "Unknown";
      bySymbolKind[symbolKind] = (bySymbolKind[symbolKind] || 0) + 1;

      const project = finding.Project || "Unknown";
      byProject[project] = (byProject[project] || 0) + 1;
    }

    const lastAnalyzed = findings.length > 0
      ? new Date().toLocaleString()
      : undefined;

    return {
      totalUnused: findings.length,
      filesAffected: uniqueFiles.size,
      bySymbolKind,
      byProject,
      lastAnalyzed
    };
  }

  async exportMetrics(): Promise<void> {
    const metrics = this.calculateMetrics();

    if (metrics.totalUnused === 0) {
      vscode.window.showWarningMessage("No metrics to export. Run analysis first.");
      return;
    }

    const lines: string[] = [];
    lines.push("# DotNetPrune Metrics Export");
    lines.push(`# Generated: ${new Date().toISOString()}`);
    lines.push("");
    lines.push("## Overview");
    lines.push(`- Total unused items: ${metrics.totalUnused}`);
    lines.push(`- Files affected: ${metrics.filesAffected}`);
    lines.push("");

    lines.push("## By Symbol Kind");
    const sortedKinds = Object.entries(metrics.bySymbolKind).sort((a, b) => b[1] - a[1]);
    for (const [kind, count] of sortedKinds) {
      lines.push(`- ${kind}: ${count}`);
    }
    lines.push("");

    lines.push("## By Project");
    const sortedProjects = Object.entries(metrics.byProject).sort((a, b) => b[1] - a[1]);
    for (const [project, count] of sortedProjects) {
      lines.push(`- ${project}: ${count}`);
    }

    const doc = await vscode.workspace.openTextDocument({
      content: lines.join("\n"),
      language: "markdown"
    });
    await vscode.window.showTextDocument(doc, { preview: false });

    vscode.window.showInformationMessage(
      `DotNetPrune: Exported metrics for ${metrics.totalUnused} items.`
    );
  }
}

class MetricsMessageItem extends vscode.TreeItem {
  constructor(message: string, state: vscode.TreeItemCollapsibleState) {
    super(message, state);
    this.contextValue = "metrics-message";
    this.iconPath = new vscode.ThemeIcon("info");
  }
}

class MetricsCategoryItem extends vscode.TreeItem {
  constructor(
    label: string,
    state: vscode.TreeItemCollapsibleState,
    iconName: string
  ) {
    super(label, state);
    this.contextValue = "metrics-category";
    this.iconPath = new vscode.ThemeIcon(iconName);
  }
}
