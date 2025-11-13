import * as vscode from "vscode";
import * as fs from "node:fs";
import * as path from "node:path";
import { spawn } from "node:child_process";

let outputChannel: vscode.OutputChannel | undefined;

type Finding = {
  Project: string;
  Solution?: string;
  FilePath: string;
  FilePathDisplay: string;
  DisplayName: string;
  ProjectFilePath: string;
  Line: number;
  SymbolKind: string;
  ContainingType: string;
  SymbolName: string;
  Accessibility: string;
  Remarks: string;
  confidence?: number;
  Icon: string;
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
    vscode.commands.registerCommand("dotnetprune.clearFindings", () =>
      provider.clear()
    ),
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
        vscode.window.showInformationMessage("DotNetPrune: File path copied to clipboard");
      }
    ),
    vscode.commands.registerCommand(
      "dotnetprune.copyProjectName",
      async (item: ProjectTreeItem) => {
        if (!item || !item.label) return;
        await vscode.env.clipboard.writeText(item.label);
        vscode.window.showInformationMessage("DotNetPrune: Project name copied to clipboard");
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

// Helper function to get appropriate icon for symbol kinds
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

class UnusedTreeProvider implements vscode.TreeDataProvider<TreeItemBase> {
  private _onDidChangeTreeData: vscode.EventEmitter<TreeItemBase | undefined> =
    new vscode.EventEmitter<TreeItemBase | undefined>();
  readonly onDidChangeTreeData: vscode.Event<TreeItemBase | undefined> =
    this._onDidChangeTreeData.event;
  private findings: Finding[] = [];
  private groupedBySolution: Map<string, Map<string, Map<string, Finding[]>>> =
    new Map();
  private solutionFiles: Map<string, string> = new Map(); // solutionName -> solutionFilePath
  private projectToSolutionMap: Map<string, string> = new Map(); // projectName -> solutionName

  constructor(private context: vscode.ExtensionContext) { }

  refresh(): void {
    this.runAnalysisAndRefresh(true).catch((err) => {
      vscode.window.showErrorMessage(
        `DotNetPrune: Failed to run analysis: ${err}`
      );
    });
  }

  clear(): void {
    this.findings = [];
    this.groupedBySolution.clear();
    this.solutionFiles.clear();
    this.projectToSolutionMap.clear();
    this._onDidChangeTreeData.fire(undefined);
    vscode.window.showInformationMessage("DotNetPrune: findings cleared.");
  }

  async runAnalysisAndRefresh(silent: boolean = false): Promise<void> {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
      vscode.window.showErrorMessage(
        "DotNetPrune: Open a workspace before running analysis."
      );
      return;
    }

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
      vscode.window.showErrorMessage(
        "DotNetPrune: Analyzer not found. Please ensure the extension is properly installed."
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
        progress.report({ message: "Executing DotNet Prune analyzer..." });

        return new Promise<boolean>((resolve) => {
          const child = spawn("dotnet", [dllPath, chosenPath], {
            cwd: getWorkspaceRootPath(),
            stdio: ["ignore", "pipe", "pipe"],
            timeout: 300000, // 5 minute timeout
          });

          let stdout = "";
          let stderr = "";

          // Handle cancellation
          token.onCancellationRequested(() => {
            child.kill();
            resolve(false);
          });

          child.stdout.on("data", (data: Buffer) => {
            stdout += data.toString();
          });

          child.stderr.on("data", (data: Buffer) => {
            stderr += data.toString();
          });

          child.on("close", async (code: number) => {
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
                throw new Error("No output received from analyzer");
              }

              // Try to parse as JSON directly
              let findings: any[];
              try {
                findings = JSON.parse(trimmedStdout);
              } catch (parseError) {
                // Fallback: try to extract JSON array if wrapped in other text
                const jsonMatch = trimmedStdout.match(/(\[[\s\S]*\])/);
                if (!jsonMatch) {
                  throw new Error(
                    `Invalid JSON output from analyzer: ${parseError}`
                  );
                }
                findings = JSON.parse(jsonMatch[1]);
              }

              // Validate findings structure
              if (!Array.isArray(findings)) {
                throw new Error(
                  "Analyzer output is not a valid findings array"
                );
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

          child.on("error", (error: Error) => {
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
      "DotNetPrune: Analysis completed."
    );

    // Open the DotNetPrune view to show the findings
    vscode.commands.executeCommand("workbench.view.dotnetprune-views");
  }

  private getDllPath(): string {
    const extensionPath = this.context.extensionPath;
    return path.join(extensionPath, "dist", "FindUnused", "FindUnused.dll");
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
          projectName = this.extractProjectNameFromPath(resolved);
        } else {
          // If project name contains path separators, extract just the project name
          if (projectName.includes(path.sep)) {
            projectName = path.basename(projectName);
            // Remove extension if present (for .csproj files)
            if (projectName.endsWith('.csproj')) {
              projectName = path.basename(projectName, '.csproj');
            }
          }
        }

        // Determine which solution this project belongs to
        const solution = this.findSolutionForProject(projectName, resolved);

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
          Icon: p.Icon ?? p.icon ?? "",
        };
      })
      .filter((finding: Finding) => {
        // Only include findings from .NET-related files
        const ext = path.extname(finding.FilePath).toLowerCase();
        const dotNetFiles = [".cs", ".sln", ".slnx", ".csproj"];
        return dotNetFiles.includes(ext);
      });

    this.findings = mapped;
    this.groupedBySolution.clear();

    // Organize findings by Solution -> Project -> File
    for (const f of this.findings) {
      const solutionName = f.Solution || this.categorizeByFilePath(f.FilePath);
      const projectName = f.Project || this.extractProjectNameFromPath(f.FilePath);

      if (!this.groupedBySolution.has(solutionName)) {
        this.groupedBySolution.set(solutionName, new Map());
      }

      const projectsMap = this.groupedBySolution.get(solutionName)!;
      if (!projectsMap.has(projectName)) {
        projectsMap.set(projectName, new Map());
      }

      const filesMap = projectsMap.get(projectName)!;
      const fileKey = f.FilePath || "(generated)";
      if (!filesMap.has(fileKey)) filesMap.set(fileKey, []);
      filesMap.get(fileKey)!.push(f);
    }
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
      this.appendToOutput(`Warning: Could not discover standalone projects: ${error}`);
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

      this.appendToOutput(`Discovered ${csprojFiles.length} projects for solution ${solutionName}`);
    } catch (error) {
      // Ignore errors in project discovery
      this.appendToOutput(`Warning: Could not discover projects for solution ${solutionName}: ${error}`);
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

  private appendToOutput(text: string) {
    if (!outputChannel) {
      outputChannel = vscode.window.createOutputChannel("DotNetPrune");
    }
    outputChannel.appendLine(text);
    outputChannel.show(true);
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
        // Use DisplayName from findings for better visibility, fallback to relative path
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
          vscode.TreeItemCollapsibleState.Collapsed
        );
        // Set tooltip to show the full file path display
        fileItem.tooltip = filePathDisplay;
        fileItems.push(fileItem);
      }
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
        ti.command = {
          command: "dotnetprune.openFinding",
          title: "Open Finding",
          arguments: [ti],
        };
        // Enhanced tooltip with new properties for better visibility
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
  constructor(
    public readonly label: string,
    public readonly filePath: string,
    public readonly solutionName: string,
    public readonly projectName: string,
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
    // Use symbol kind icon, and incorporate analyzer icon into label if provided
    this.iconPath = getIconForSymbolKind(finding.SymbolKind);
    if (finding.Icon) {
      this.label = `${finding.Icon} ${label}`;
    }
    // The command to open the finding is set by the provider
  }
}
