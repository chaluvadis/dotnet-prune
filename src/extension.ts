import * as vscode from "vscode";
import * as fs from "node:fs";
import * as path from "node:path";
import { spawn } from "node:child_process";

// Helper function to get workspace root path safely using modern API
function getWorkspaceRootPath(): string {
  const workspaceFolders = vscode.workspace.workspaceFolders;
  if (!workspaceFolders || workspaceFolders.length === 0) {
    return ".";
  }
  return workspaceFolders[0].uri.fsPath;
}

let outputChannel: vscode.OutputChannel | undefined;

type Finding = {
  Project: string;
  FilePath: string;
  Line: number;
  SymbolKind: string;
  ContainingType: string;
  SymbolName: string;
  Accessibility: string;
  Remarks: string;
  confidence?: number;
};

export function activate(context: vscode.ExtensionContext) {
  const provider = new UnusedTreeProvider(context);
  const treeView = vscode.window.createTreeView("dotnetprune-findings", {
    treeDataProvider: provider,
    showCollapseAll: true,
  });

  context.subscriptions.push(
    treeView,
    vscode.commands.registerCommand("dotnetprune.refresh", () =>
      provider.refresh()
    ),
    vscode.commands.registerCommand("dotnetprune.runAnalysis", async () => {
      await provider.runAnalysisAndRefresh();
    }),
    vscode.commands.registerCommand("dotnetprune.openReport", async () => {
      await provider.openReportFile();
    }),
    vscode.commands.registerCommand("dotnetprune.clearFindings", () =>
      provider.clear()
    ),
    vscode.commands.registerCommand(
      "dotnetprune.openFinding",
      async (item: FindingTreeItem) => {
        if (!item) return;
        await provider.openFinding(item.finding);
      }
    )
  );

  // initial load
  provider.refresh();
}

export function deactivate() {
  if (outputChannel) {
    outputChannel.dispose();
    outputChannel = undefined;
  }
}

class UnusedTreeProvider implements vscode.TreeDataProvider<TreeItemBase> {
  private _onDidChangeTreeData: vscode.EventEmitter<TreeItemBase | undefined> =
    new vscode.EventEmitter<TreeItemBase | undefined>();
  readonly onDidChangeTreeData: vscode.Event<TreeItemBase | undefined> =
    this._onDidChangeTreeData.event;
  private findings: Finding[] = [];
  private groupedByProject: Map<string, Map<string, Finding[]>> = new Map();

  constructor(private context: vscode.ExtensionContext) {}

  refresh(): void {
    this.runAnalysisAndRefresh(true).catch((err) => {
      vscode.window.showErrorMessage(
        `DotNetPrune: Failed to run analysis: ${err}`
      );
    });
  }

  clear(): void {
    this.findings = [];
    this.groupedByProject.clear();
    this._onDidChangeTreeData.fire(undefined);
    vscode.window.showInformationMessage("DotNetPrune: findings cleared.");
  }

  async runAnalysisAndRefresh(silent: boolean = false): Promise<void> {
    const config = vscode.workspace.getConfiguration("dotNetPrune");
    const reportPathSetting = config.get<string>("reportPath") ?? "";

    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
      vscode.window.showErrorMessage(
        "DotNetPrune: Open a workspace before running analysis."
      );
      return;
    }

    // discover solution/csproj files
    const slnxCandidates = await vscode.workspace.findFiles(
      "**/*.slnx",
      "**/node_modules/**",
      10
    );
    const slnCandidates = await vscode.workspace.findFiles(
      "**/*.sln",
      "**/node_modules/**",
      10
    );
    const csprojCandidates = await vscode.workspace.findFiles(
      "**/*.csproj",
      "**/node_modules/**",
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

    // resolve report path
    const defaultReport =
      reportPathSetting && reportPathSetting.trim() !== ""
        ? reportPathSetting
        : path.join(getWorkspaceRootPath(), "dotnetprune-report.json");

    const dllPath = this.getDllPath();
    if (!dllPath || !fs.existsSync(dllPath)) {
      vscode.window.showErrorMessage(
        "DotNetPrune: FindUnused.dll not found. Please ensure the extension is properly installed."
      );
      return;
    }

    // Use spawn for better security and control
    const run = await vscode.window.withProgress(
      {
        location: vscode.ProgressLocation.Notification,
        title: "DotNetPrune: running analysis",
        cancellable: true,
      },
      async (progress, token) => {
        progress.report({ message: "Executing FindUnused analyzer..." });

        return new Promise<boolean>((resolve) => {
          const child = spawn('dotnet', [dllPath, chosenPath], {
            cwd: getWorkspaceRootPath(),
            stdio: ['ignore', 'pipe', 'pipe'],
            timeout: 300000, // 5 minute timeout
          });

          let stdout = '';
          let stderr = '';

          // Handle cancellation
          token.onCancellationRequested(() => {
            child.kill();
            resolve(false);
          });

          child.stdout.on('data', (data: Buffer) => {
            stdout += data.toString();
          });

          child.stderr.on('data', (data: Buffer) => {
            stderr += data.toString();
          });

          child.on('close', async (code: number) => {
            try {
              // Check exit code - code 1 means findings detected (success), other codes are errors
              if (code !== 0 && code !== 1) {
                throw new Error(`Analyzer exited with code ${code}`);
              }

              // Log stderr if present
              if (stderr && stderr.trim().length > 0) {
                this.appendToOutput(stderr);
              }

              // Parse JSON from stdout - expect clean JSON array
              const trimmedStdout = stdout.trim();
              if (!trimmedStdout) {
                throw new Error('No output received from analyzer');
              }

              // Try to parse as JSON directly
              let findings: any[];
              try {
                findings = JSON.parse(trimmedStdout);
              } catch (parseError) {
                // Fallback: try to extract JSON array if wrapped in other text
                const jsonMatch = trimmedStdout.match(/(\[[\s\S]*\])/);
                if (!jsonMatch) {
                  throw new Error(`Invalid JSON output from analyzer: ${parseError}`);
                }
                findings = JSON.parse(jsonMatch[1]);
              }

              // Validate findings structure
              if (!Array.isArray(findings)) {
                throw new Error('Analyzer output is not a valid findings array');
              }

              await this.loadFindingsFromJson(findings);
              resolve(true);

            } catch (error: any) {
              const errorMsg = `Failed to parse analyzer output: ${error.message}`;
              vscode.window.showErrorMessage(`DotNetPrune: ${errorMsg}`);
              this.appendToOutput(errorMsg);
              this.appendToOutput(`Raw stdout: ${stdout.substring(0, 1000)}`);
              resolve(false);
            }
          });

          child.on('error', (error: Error) => {
            const errorMsg = `Failed to execute analyzer: ${error.message}`;
            vscode.window.showErrorMessage(`DotNetPrune: ${errorMsg}`);
            this.appendToOutput(errorMsg);
            resolve(false);
          });
        });
      }
    );

    if (!run) return;

    this._onDidChangeTreeData.fire(undefined);
    vscode.window.showInformationMessage(
      "DotNetPrune: analysis complete and data loaded."
    );

    // Open the DotNetPrune view to show the findings
    vscode.commands.executeCommand('workbench.view.dotnetprune-views');
  }

  async openReportFile(): Promise<void> {
    const config = vscode.workspace.getConfiguration("dotNetPrune");
    const reportPathSetting = config.get<string>("reportPath") ?? "";
    const reportPath =
      reportPathSetting && reportPathSetting.trim() !== ""
        ? path.isAbsolute(reportPathSetting)
          ? reportPathSetting
          : path.join(getWorkspaceRootPath(), reportPathSetting)
        : path.join(getWorkspaceRootPath(), "dotnetprune-report.json");

    if (!fs.existsSync(reportPath)) {
      vscode.window.showWarningMessage(
        `DotNetPrune: report not found at ${reportPath}`
      );
      return;
    }
    const doc = await vscode.workspace.openTextDocument(reportPath);
    await vscode.window.showTextDocument(doc, { preview: true });
  }

  private getDllPath(): string {
    // The FindUnused.dll is packaged in the extension directory
    const extensionPath = this.context.extensionPath;
    const dllPath = path.join(extensionPath, "FindUnused", "FindUnused.dll");

    if (fs.existsSync(dllPath)) {
      return dllPath;
    }

    // Development fallback - check if we're in development mode
    const devPath = path.join(extensionPath, "FindUnused", "FindUnused.dll");
    if (fs.existsSync(devPath)) {
      return devPath;
    }

    return ""; // Return empty string to indicate not found
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
      if (relativePath.startsWith('..') || path.isAbsolute(relativePath)) {
        return false; // Path is outside workspace
      }

      // Check file extension
      const ext = path.extname(filePath).toLowerCase();
      return ['.sln', '.slnx', '.csproj'].includes(ext);

    } catch (error) {
      return false;
    }
  }


  private async loadFindingsFromJson(findingsJson: any[]): Promise<void> {
    if (!Array.isArray(findingsJson)) {
      throw new Error("Findings JSON must be an array.");
    }

    // Map to internal Finding type and normalize paths
    const mapped: Finding[] = findingsJson.map((p: any) => {
      const filePath = p.FilePath ?? p.filePath ?? "";
      const resolved = path.isAbsolute(filePath)
        ? filePath
        : path.join(getWorkspaceRootPath(), filePath);
      return {
        Project: p.Project ?? p.project ?? "",
        FilePath: resolved,
        Line: typeof p.Line === "number" ? p.Line : p.line ?? 1,
        SymbolKind: p.SymbolKind ?? p.symbolKind ?? "",
        ContainingType: p.ContainingType ?? p.containingType ?? "",
        SymbolName: p.SymbolName ?? p.symbolName ?? "",
        Accessibility: p.Accessibility ?? p.accessibility ?? "",
        Remarks: p.Remarks ?? p.remarks ?? "",
        confidence: typeof p.confidence === "number" ? p.confidence : undefined,
      };
    });

    this.findings = mapped;
    this.groupedByProject.clear();

    for (const f of this.findings) {
      const proj = f.Project || "Unknown";
      if (!this.groupedByProject.has(proj))
        this.groupedByProject.set(proj, new Map());
      const byFile = this.groupedByProject.get(proj)!;
      const fileKey = f.FilePath || "(generated)";
      if (!byFile.has(fileKey)) byFile.set(fileKey, []);
      byFile.get(fileKey)!.push(f);
    }
  }

  // open a finding in editor and reveal the line
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

  private appendToOutput(text: string) {
    if (!outputChannel) {
      outputChannel = vscode.window.createOutputChannel("DotNetPrune");
    }
    outputChannel.appendLine(text);
    outputChannel.show(true);
  }

  // TreeDataProvider implementation

  getTreeItem(element: TreeItemBase): vscode.TreeItem {
    return element;
  }

  getChildren(element?: TreeItemBase): Thenable<TreeItemBase[]> {
    if (!element) {
      // top-level: projects
      const items = Array.from(this.groupedByProject.keys()).map((proj) => {
        const item = new ProjectTreeItem(
          proj,
          vscode.TreeItemCollapsibleState.Collapsed
        );
        return item;
      });
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

    if (element instanceof ProjectTreeItem) {
      const proj = element.label as string;
      const files = this.groupedByProject.get(proj);
      if (!files) return Promise.resolve([]);
      const fileItems: TreeItemBase[] = [];
      for (const [filePath, findings] of files) {
        const fileLabel = path.relative(getWorkspaceRootPath(), filePath);
        const fileItem = new FileTreeItem(
          fileLabel,
          filePath,
          vscode.TreeItemCollapsibleState.Collapsed
        );
        fileItems.push(fileItem);
      }
      return Promise.resolve(fileItems);
    }

    if (element instanceof FileTreeItem) {
      const filePath = element.filePath;
      // find entries
      const projEntry = Array.from(this.groupedByProject.entries()).find(
        ([, files]) => files.has(filePath)
      );
      if (!projEntry) return Promise.resolve([]);
      const findings = projEntry[1].get(filePath) || [];
      const items = findings.map((f) => {
        const label = `${f.SymbolKind}: ${f.SymbolName}`;
        const ti = new FindingTreeItem(
          label,
          f,
          vscode.TreeItemCollapsibleState.None
        );
        ti.command = {
          command: "dotnetprune.openFinding",
          title: "Open Finding",
          arguments: [ti],
        };
        ti.tooltip = `${f.ContainingType} — ${f.Remarks}`;
        ti.description = `Ln ${f.Line} (${f.Accessibility})`;
        return ti;
      });
      return Promise.resolve(items);
    }

    return Promise.resolve([]);
  }
}

abstract class TreeItemBase extends vscode.TreeItem {}

class MessageTreeItem extends TreeItemBase {
  constructor(message: string, state: vscode.TreeItemCollapsibleState) {
    super(message, state);
    this.contextValue = "message";
    this.iconPath = new vscode.ThemeIcon("info");
  }
}

class ProjectTreeItem extends TreeItemBase {
  constructor(
    public readonly label: string,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(label, state);
    this.contextValue = "project";
    this.iconPath = new vscode.ThemeIcon("project");
  }
}

class FileTreeItem extends TreeItemBase {
  constructor(
    public readonly label: string,
    public readonly filePath: string,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(label, state);
    this.contextValue = "file";
    this.iconPath = new vscode.ThemeIcon("file");
    // open on double click? handled by child items commands
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
    this.iconPath = new vscode.ThemeIcon("warning"); // severity-neutral; you can change based on accessibility/confidence
    // The command to open the finding is set by the provider
  }
}
