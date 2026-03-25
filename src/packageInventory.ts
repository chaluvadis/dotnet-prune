import * as vscode from "vscode";
import * as fs from "node:fs";
import * as path from "node:path";

// ─── Types ────────────────────────────────────────────────────────────────────

export type PackageReference = {
  include: string;
  version?: string;
  privateAssets?: string;
  includeAssets?: string;
  condition?: string;
};

export type ProjectPackageInfo = {
  projectName: string;
  projectPath: string;
  targetFrameworks: string[];
  unusedPackages: PackageReference[];
  referencedPackages: PackageReference[];
};

export type SolutionInventory = {
  solutionPath: string;
  solutionName: string;
  projects: ProjectPackageInfo[];
};

// ─── Solution Parser ──────────────────────────────────────────────────────────

/**
 * Parses .sln and .slnx files to extract referenced project paths.
 */
export class SolutionParser {
  /** Returns relative project paths from a .sln file */
  static parseSlnProjects(slnContent: string): string[] {
    const projects: string[] = [];
    // Match: Project("{...}") = "Name", "path\to\Project.csproj", "{GUID}"
    const projectRegex =
      /Project\("[^"]*"\)\s*=\s*"[^"]*",\s*"([^"]+\.csproj)"/gi;
    let match: RegExpExecArray | null;
    while ((match = projectRegex.exec(slnContent)) !== null) {
      // Normalize backslashes to platform separator
      projects.push(match[1].replace(/\\/g, path.sep));
    }
    return projects;
  }

  /** Returns relative project paths from a .slnx file (XML format) */
  static parseSlnxProjects(slnxContent: string): string[] {
    const projects: string[] = [];
    // Match: <Project Path="relative/path/Project.csproj" ... />
    const projectRegex = /<Project\s[^>]*Path="([^"]+\.csproj)"[^>]*/gi;
    let match: RegExpExecArray | null;
    while ((match = projectRegex.exec(slnxContent)) !== null) {
      projects.push(match[1].replace(/\\/g, path.sep));
    }
    return projects;
  }

  /** Parse a solution file and return absolute project paths that exist on disk */
  static getProjectPaths(solutionPath: string): string[] {
    try {
      const content = fs.readFileSync(solutionPath, "utf-8");
      const solutionDir = path.dirname(solutionPath);
      const ext = path.extname(solutionPath).toLowerCase();
      const relativePaths =
        ext === ".slnx"
          ? this.parseSlnxProjects(content)
          : this.parseSlnProjects(content);

      return relativePaths
        .map((rel) => path.resolve(solutionDir, rel))
        .filter((abs) => {
          try {
            return fs.existsSync(abs);
          } catch {
            return false;
          }
        });
    } catch {
      return [];
    }
  }
}

// ─── Csproj Parser ────────────────────────────────────────────────────────────

/**
 * Parses .csproj files to extract PackageReference elements and target frameworks.
 */
export class CsprojParser {
  /** Extract all PackageReference elements from a .csproj file content */
  static getPackageReferences(csprojContent: string): PackageReference[] {
    const packages: PackageReference[] = [];

    const getAttr = (attrs: string, name: string): string | undefined => {
      const m = new RegExp(`${name}\\s*=\\s*"([^"]*)"`, "i").exec(attrs);
      return m?.[1];
    };

    const getInnerTag = (
      innerContent: string,
      name: string
    ): string | undefined => {
      const m = new RegExp(`<${name}>([^<]*)<\\/${name}>`, "i").exec(
        innerContent
      );
      return m?.[1];
    };

    const processPackage = (
      attrs: string,
      innerContent?: string
    ): PackageReference | null => {
      const include = getAttr(attrs, "Include");
      if (!include) return null;
      return {
        include,
        version:
          getAttr(attrs, "Version") ??
          (innerContent ? getInnerTag(innerContent, "Version") : undefined),
        privateAssets:
          getAttr(attrs, "PrivateAssets") ??
          (innerContent
            ? getInnerTag(innerContent, "PrivateAssets")
            : undefined),
        includeAssets:
          getAttr(attrs, "IncludeAssets") ??
          (innerContent
            ? getInnerTag(innerContent, "IncludeAssets")
            : undefined),
        condition: getAttr(attrs, "Condition"),
      };
    };

    // Self-closing: <PackageReference Include="..." ... />
    const selfClosing = /<PackageReference\s+([^>\/]+)\/>/gi;
    let match: RegExpExecArray | null;
    while ((match = selfClosing.exec(csprojContent)) !== null) {
      const pkg = processPackage(match[1]);
      if (pkg) packages.push(pkg);
    }

    // Open/close: <PackageReference ...>...</PackageReference>
    const openClose =
      /<PackageReference\s+([^>\/]+)>([\s\S]*?)<\/PackageReference>/gi;
    while ((match = openClose.exec(csprojContent)) !== null) {
      const pkg = processPackage(match[1], match[2]);
      if (pkg) packages.push(pkg);
    }

    return packages;
  }

  /** Extract target framework(s) from a .csproj file content */
  static getTargetFrameworks(csprojContent: string): string[] {
    const single = /<TargetFramework>([^<]+)<\/TargetFramework>/i.exec(
      csprojContent
    );
    if (single) return [single[1].trim()];

    const multi = /<TargetFrameworks>([^<]+)<\/TargetFrameworks>/i.exec(
      csprojContent
    );
    if (multi) {
      return multi[1]
        .split(";")
        .map((f) => f.trim())
        .filter(Boolean);
    }
    return [];
  }
}

// ─── Package Usage Analyzer ───────────────────────────────────────────────────

/**
 * Determines which packages are "used" vs "unused" by performing a static
 * text search across .cs files in the project directory.
 *
 * This is a Phase 1 heuristic — no write operations, no dotnet execution.
 */
export class PackageUsageAnalyzer {
  private static readonly SKIP_DIRS = new Set([
    "obj",
    "bin",
    "node_modules",
    ".git",
  ]);

  /** Collect all .cs file contents from a project directory tree */
  private static collectCsContents(projectDir: string): string {
    const parts: string[] = [];
    try {
      const entries = fs.readdirSync(projectDir, { withFileTypes: true });
      for (const entry of entries) {
        const fullPath = path.join(projectDir, entry.name);
        if (entry.isFile() && entry.name.endsWith(".cs")) {
          try {
            parts.push(fs.readFileSync(fullPath, "utf-8"));
          } catch {
            // skip unreadable files
          }
        } else if (
          entry.isDirectory() &&
          !PackageUsageAnalyzer.SKIP_DIRS.has(entry.name.toLowerCase())
        ) {
          parts.push(PackageUsageAnalyzer.collectCsContents(fullPath));
        }
      }
    } catch {
      // ignore unreadable directories
    }
    return parts.join("\n");
  }

  /**
   * Determine if a package appears to be used in the project source files.
   * Heuristic: packages marked as analyzer/tool-only (PrivateAssets=all)
   * are always considered referenced. For others, look for the package name
   * (or first namespace segment) in using directives or type references.
   */
  static isPackageUsed(pkg: PackageReference, csContent: string): boolean {
    // Build/analyzer packages intentionally excluded from compile output
    if (pkg.privateAssets?.toLowerCase() === "all") {
      return true;
    }

    const id = pkg.include;
    // Unique search terms: full name and root namespace
    const rootNs = id.split(".")[0];
    const terms = id !== rootNs ? [id, rootNs] : [id];

    return terms.some((term) => {
      // Escape all regex metacharacters in the package name/namespace
      const escaped = term.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      // Match `using Namespace`, `using Namespace.`, or standalone identifier
      return (
        new RegExp(`using\\s+${escaped}(\\s|;|\\.)`, "i").test(csContent) ||
        new RegExp(`\\b${escaped}\\b`, "i").test(csContent)
      );
    });
  }

  /** Analyze a project and split packages into unused/referenced lists */
  static analyzeProject(
    projectPath: string,
    packages: PackageReference[]
  ): { unusedPackages: PackageReference[]; referencedPackages: PackageReference[] } {
    const projectDir = path.dirname(projectPath);
    const csContent = this.collectCsContents(projectDir);

    const unusedPackages: PackageReference[] = [];
    const referencedPackages: PackageReference[] = [];

    for (const pkg of packages) {
      if (this.isPackageUsed(pkg, csContent)) {
        referencedPackages.push(pkg);
      } else {
        unusedPackages.push(pkg);
      }
    }
    return { unusedPackages, referencedPackages };
  }
}

// ─── TreeItem Classes ─────────────────────────────────────────────────────────

abstract class PkgTreeItemBase extends vscode.TreeItem {}

export class SolutionPackageTreeItem extends PkgTreeItemBase {
  constructor(
    public readonly solutionName: string,
    public readonly solutionPath: string,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(solutionName, state);
    this.contextValue = "pkg-solution";
    this.iconPath = new vscode.ThemeIcon("root-folder");
    this.tooltip = solutionPath;
  }
}

export class ProjectPackageTreeItem extends PkgTreeItemBase {
  constructor(
    public readonly projectName: string,
    public readonly solutionPath: string,
    public readonly projectInfo: ProjectPackageInfo,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(projectName, state);
    this.contextValue = "pkg-project";
    this.iconPath = new vscode.ThemeIcon("project");
    const fw = projectInfo.targetFrameworks.join(", ");
    this.description = fw || undefined;
    this.tooltip = projectInfo.projectPath;
  }
}

export class PackageGroupTreeItem extends PkgTreeItemBase {
  constructor(
    public readonly groupType: "unused" | "referenced",
    public readonly count: number,
    public readonly solutionPath: string,
    public readonly projectInfo: ProjectPackageInfo,
    state: vscode.TreeItemCollapsibleState
  ) {
    const label =
      groupType === "unused"
        ? `Unused Package References (${count})`
        : `Referenced Packages (${count})`;
    super(label, state);
    this.contextValue =
      groupType === "unused" ? "pkg-group-unused" : "pkg-group-referenced";
    this.iconPath =
      groupType === "unused"
        ? new vscode.ThemeIcon("warning")
        : new vscode.ThemeIcon("list-unordered");
  }
}

export class PackageTreeItem extends PkgTreeItemBase {
  constructor(
    public readonly pkg: PackageReference,
    public readonly isUnused: boolean,
    state: vscode.TreeItemCollapsibleState
  ) {
    super(pkg.include, state);
    this.contextValue = isUnused ? "pkg-unused" : "pkg-referenced";
    this.iconPath = new vscode.ThemeIcon("package");
    this.description = pkg.version ?? "";
    const details: string[] = [];
    if (pkg.version) details.push(`Version: ${pkg.version}`);
    if (pkg.privateAssets) details.push(`PrivateAssets: ${pkg.privateAssets}`);
    if (pkg.includeAssets) details.push(`IncludeAssets: ${pkg.includeAssets}`);
    if (pkg.condition) details.push(`Condition: ${pkg.condition}`);
    this.tooltip = details.join("\n") || pkg.include;
  }
}

export class EmptyStatePackageTreeItem extends PkgTreeItemBase {
  constructor(message: string) {
    super(message, vscode.TreeItemCollapsibleState.None);
    this.contextValue = "pkg-empty";
    this.iconPath = new vscode.ThemeIcon("check");
  }
}

export class MessagePackageTreeItem extends PkgTreeItemBase {
  constructor(message: string) {
    super(message, vscode.TreeItemCollapsibleState.None);
    this.contextValue = "pkg-message";
    this.iconPath = new vscode.ThemeIcon("info");
  }
}

// ─── Package Inventory Provider ───────────────────────────────────────────────

export class PackageInventoryProvider
  implements vscode.TreeDataProvider<PkgTreeItemBase>
{
  private _onDidChangeTreeData = new vscode.EventEmitter<
    PkgTreeItemBase | undefined
  >();
  readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

  private inventories: SolutionInventory[] = [];
  private isAnalysisRunning = false;
  private outputChannel: vscode.OutputChannel | undefined;

  constructor(_context: vscode.ExtensionContext) {}

  private log(
    text: string,
    level: "error" | "warn" | "info" | "debug" = "info"
  ): void {
    if (!this.outputChannel) {
      this.outputChannel = vscode.window.createOutputChannel("DotNetPrune");
    }
    this.outputChannel.appendLine(`[${level.toUpperCase()}] ${text}`);
    if (level === "error" || level === "warn") {
      this.outputChannel.show(true);
    }
  }

  getTreeItem(element: PkgTreeItemBase): vscode.TreeItem {
    return element;
  }

  getChildren(element?: PkgTreeItemBase): Thenable<PkgTreeItemBase[]> {
    if (!element) {
      if (this.inventories.length === 0) {
        return Promise.resolve([
          new MessagePackageTreeItem(
            "No package data. Run $(search) Analyze Packages."
          ),
        ]);
      }
      return Promise.resolve(
        this.inventories.map(
          (inv) =>
            new SolutionPackageTreeItem(
              inv.solutionName,
              inv.solutionPath,
              vscode.TreeItemCollapsibleState.Expanded
            )
        )
      );
    }

    if (element instanceof SolutionPackageTreeItem) {
      const inv = this.inventories.find(
        (i) => i.solutionPath === element.solutionPath
      );
      if (!inv) return Promise.resolve([]);
      if (inv.projects.length === 0) {
        return Promise.resolve([
          new MessagePackageTreeItem("No projects found in solution."),
        ]);
      }
      return Promise.resolve(
        inv.projects.map(
          (proj) =>
            new ProjectPackageTreeItem(
              proj.projectName,
              inv.solutionPath,
              proj,
              vscode.TreeItemCollapsibleState.Collapsed
            )
        )
      );
    }

    if (element instanceof ProjectPackageTreeItem) {
      const proj = element.projectInfo;
      const totalPackages =
        proj.unusedPackages.length + proj.referencedPackages.length;
      if (totalPackages === 0) {
        return Promise.resolve([
          new MessagePackageTreeItem("No package references found."),
        ]);
      }

      // Unused group first, then referenced
      return Promise.resolve([
        new PackageGroupTreeItem(
          "unused",
          proj.unusedPackages.length,
          element.solutionPath,
          proj,
          proj.unusedPackages.length > 0
            ? vscode.TreeItemCollapsibleState.Expanded
            : vscode.TreeItemCollapsibleState.None
        ),
        new PackageGroupTreeItem(
          "referenced",
          proj.referencedPackages.length,
          element.solutionPath,
          proj,
          vscode.TreeItemCollapsibleState.Collapsed
        ),
      ]);
    }

    if (element instanceof PackageGroupTreeItem) {
      const packages =
        element.groupType === "unused"
          ? element.projectInfo.unusedPackages
          : element.projectInfo.referencedPackages;

      if (packages.length === 0) {
        return Promise.resolve([
          new EmptyStatePackageTreeItem(
            element.groupType === "unused"
              ? "No unused packages"
              : "No referenced packages"
          ),
        ]);
      }
      return Promise.resolve(
        packages.map(
          (pkg) =>
            new PackageTreeItem(
              pkg,
              element.groupType === "unused",
              vscode.TreeItemCollapsibleState.None
            )
        )
      );
    }

    return Promise.resolve([]);
  }

  refresh(): void {
    this.runAnalysis(true).catch((err) => {
      vscode.window.showErrorMessage(
        `DotNetPrune: Package analysis failed: ${err}`
      );
    });
  }

  clear(): void {
    this.inventories = [];
    this._onDidChangeTreeData.fire(undefined);
  }

  async runAnalysis(silent: boolean = false): Promise<void> {
    if (!vscode.workspace.isTrusted) {
      if (!silent) {
        vscode.window.showWarningMessage(
          "DotNetPrune: Analysis requires a trusted workspace."
        );
      }
      return;
    }

    if (this.isAnalysisRunning) {
      if (!silent) {
        vscode.window.showWarningMessage(
          "DotNetPrune: Package analysis is already in progress."
        );
      }
      return;
    }

    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
      vscode.window.showErrorMessage(
        "DotNetPrune: Open a workspace before running analysis."
      );
      return;
    }

    const excludedFolders =
      "**/{bin,debug,obj,release,nuget}/**";
    const [slnxCandidates, slnCandidates] = await Promise.all([
      vscode.workspace.findFiles(
        "**/*.slnx",
        `${excludedFolders},**/node_modules/**`,
        10
      ),
      vscode.workspace.findFiles(
        "**/*.sln",
        `${excludedFolders},**/node_modules/**`,
        10
      ),
    ]);

    const allSolutions = [...slnxCandidates, ...slnCandidates];
    if (allSolutions.length === 0) {
      vscode.window.showWarningMessage(
        "DotNetPrune: No .sln/.slnx found in workspace. Please add a solution file."
      );
      return;
    }

    let chosenSolution: vscode.Uri;
    if (allSolutions.length === 1 || silent) {
      chosenSolution = allSolutions[0];
    } else {
      const workspaceRoot = workspaceFolders[0].uri.fsPath;
      const picks = allSolutions.map((u) => ({
        label: path.relative(workspaceRoot, u.fsPath),
        uri: u,
      }));
      const sel = await vscode.window.showQuickPick(picks, {
        placeHolder: "Select solution to analyze packages",
      });
      if (!sel) return;
      chosenSolution = sel.uri;
    }

    this.isAnalysisRunning = true;
    try {
      const inventory = await vscode.window.withProgress(
        {
          location: vscode.ProgressLocation.Notification,
          title: "DotNetPrune:",
          cancellable: false,
        },
        async (progress) => {
          progress.report({ message: "Analyzing package references..." });
          return this.buildInventory(chosenSolution.fsPath);
        }
      );

      this.inventories = [inventory];
      this._onDidChangeTreeData.fire(undefined);

      const totalUnused = inventory.projects.reduce(
        (sum, p) => sum + p.unusedPackages.length,
        0
      );
      vscode.window.showInformationMessage(
        `DotNetPrune: Package analysis complete. ${inventory.projects.length} project(s), ${totalUnused} unused package reference(s) found.`
      );
      vscode.commands.executeCommand("workbench.view.dotnetprune-views");
    } finally {
      this.isAnalysisRunning = false;
    }
  }

  private buildInventory(solutionPath: string): SolutionInventory {
    const solutionName = path.basename(
      solutionPath,
      path.extname(solutionPath)
    );
    this.log(`Analyzing solution: ${solutionName}`, "info");

    const projectPaths = SolutionParser.getProjectPaths(solutionPath);
    this.log(
      `Found ${projectPaths.length} project(s) in ${solutionName}`,
      "info"
    );

    const projects: ProjectPackageInfo[] = [];
    for (const projectPath of projectPaths) {
      try {
        const projectName = path.basename(projectPath, ".csproj");
        const content = fs.readFileSync(projectPath, "utf-8");
        const packages = CsprojParser.getPackageReferences(content);
        const targetFrameworks = CsprojParser.getTargetFrameworks(content);
        const { unusedPackages, referencedPackages } =
          PackageUsageAnalyzer.analyzeProject(projectPath, packages);

        projects.push({
          projectName,
          projectPath,
          targetFrameworks,
          unusedPackages,
          referencedPackages,
        });

        this.log(
          `  ${projectName}: ${packages.length} package(s) — ${unusedPackages.length} unused, ${referencedPackages.length} referenced`,
          "info"
        );
      } catch (err) {
        // Non-fatal: log and continue with remaining projects
        this.log(
          `  Warning: Could not analyze ${projectPath}: ${err}`,
          "warn"
        );
      }
    }

    return { solutionPath, solutionName, projects };
  }
}
