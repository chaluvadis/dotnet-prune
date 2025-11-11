import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import { exec } from "child_process";
import { promisify } from "util";

const execAsync = promisify(exec);

type Finding = {
  Project: string;
  FilePath: string;
  Line: number;
  SymbolKind: string;
  ContainingType: string;
  SymbolName: string;
  Accessibility: string;
  Remarks: string;
  // optional: confidence?: number;
};

type SolutionInfo = {
  path: string;
  type: 'sln' | 'slnx' | 'csproj';
  name: string;
};

const OUTPUT_CHANNEL_NAME = "DotNetPrune";
const FINDUNUSED_EXE = "dotnet"; // We'll call the FindUnused.dll as a dotnet tool

let diagnosticCollection: vscode.DiagnosticCollection;
let outputChannel: vscode.OutputChannel;
let fileWatchers: vscode.FileSystemWatcher[] = [];

/**
 * Auto-discover solution files and run analysis
 */
async function autoDiscoverAndAnalyzeSolutions(context: vscode.ExtensionContext) {
  const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
  if (!workspaceFolder) {
    outputChannel.appendLine("No workspace folder found for auto-discovery");
    return;
  }

  outputChannel.show(true);
  outputChannel.appendLine("🔍 Auto-discovering .NET solutions and projects...");

  const solutions = await discoverSolutions(workspaceFolder.uri.fsPath);
  
  if (solutions.length === 0) {
    outputChannel.appendLine("No .NET solution or project files found (.sln, .slnx, .csproj)");
    return;
  }

  outputChannel.appendLine(`Found ${solutions.length} solution(s)/project(s):`);
  for (const solution of solutions) {
    outputChannel.appendLine(`  📁 ${solution.name} (${solution.type.toUpperCase()})`);
  }

  // Setup file watchers for auto-reanalysis
  setupFileWatchers(context, solutions);

  // Run analysis for each solution
  for (const solution of solutions) {
    outputChannel.appendLine(`\n🔄 Starting analysis for: ${solution.name}`);
    await runAnalysisForSolution(solution);
  }
}

/**
 * Discover all solution and project files in the workspace
 */
async function discoverSolutions(workspacePath: string): Promise<SolutionInfo[]> {
  const solutions: SolutionInfo[] = [];
  
  try {
    // Find .sln files
    const slnFiles = await vscode.workspace.findFiles(
      new vscode.RelativePattern(workspacePath, "**/*.sln"),
      "**/node_modules/**"
    );
    
    for (const uri of slnFiles) {
      const fileName = path.basename(uri.fsPath);
      solutions.push({
        path: uri.fsPath,
        type: 'sln',
        name: fileName
      });
    }

    // Find .slnx files
    const slnxFiles = await vscode.workspace.findFiles(
      new vscode.RelativePattern(workspacePath, "**/*.slnx"),
      "**/node_modules/**"
    );
    
    for (const uri of slnxFiles) {
      const fileName = path.basename(uri.fsPath);
      solutions.push({
        path: uri.fsPath,
        type: 'slnx',
        name: fileName
      });
    }

    // Find .csproj files (single project files)
    const csprojFiles = await vscode.workspace.findFiles(
      new vscode.RelativePattern(workspacePath, "**/*.csproj"),
      "**/node_modules/**"
    );
    
    for (const uri of csprojFiles) {
      const fileName = path.basename(uri.fsPath);
      // Only add csproj if there's no corresponding sln file in the same directory
      const dirName = path.dirname(uri.fsPath);
      const slnInSameDir = path.join(dirName, path.basename(dirName) + '.sln');
      const slnxInSameDir = path.join(dirName, path.basename(dirName) + '.slnx');
      
      if (!fs.existsSync(slnInSameDir) && !fs.existsSync(slnxInSameDir)) {
        solutions.push({
          path: uri.fsPath,
          type: 'csproj',
          name: fileName
        });
      }
    }
  } catch (error) {
    outputChannel.appendLine(`Error discovering solutions: ${error}`);
  }

  return solutions;
}

/**
 * Setup file watchers to trigger re-analysis when solution files change
 */
function setupFileWatchers(context: vscode.ExtensionContext, solutions: SolutionInfo[]) {
  // Clear existing watchers
  fileWatchers.forEach(watcher => watcher.dispose());
  fileWatchers = [];

  for (const solution of solutions) {
    // Watch for changes to solution/project files
    const watcher = vscode.workspace.createFileSystemWatcher(
      new vscode.RelativePattern(solution.path, "*"),
      false, // ignoreCreate
      true,  // ignoreChange
      false  // ignoreDelete
    );

    watcher.onDidChange(async () => {
      outputChannel.appendLine(`📝 Solution file changed: ${solution.name}. Re-running analysis...`);
      await runAnalysisForSolution(solution);
    });

    // Watch for changes to source files in the solution
    const sourceWatcher = vscode.workspace.createFileSystemWatcher(
      new vscode.RelativePattern(path.dirname(solution.path), "**/*.{cs,razor,xaml}"),
      false,
      true,
      false
    );

    sourceWatcher.onDidChange(async () => {
      // Debounce re-analysis to avoid excessive runs
      setTimeout(async () => {
        outputChannel.appendLine(`📝 Source files changed in ${solution.name}. Re-running analysis...`);
        await runAnalysisForSolution(solution);
      }, 2000);
    });

    fileWatchers.push(watcher, sourceWatcher);
    context.subscriptions.push(watcher, sourceWatcher);
  }
}

/**
 * Run analysis for a specific solution
 */
async function runAnalysisForSolution(solution: SolutionInfo) {
  const findUnusedPath = resolveFindUnusedPath();
  if (!findUnusedPath) {
    outputChannel.appendLine(`❌ Could not find FindUnused.Console.dll for ${solution.name}`);
    return;
  }

  const config = vscode.workspace.getConfiguration("dotNetPrune");
  const includePublic = config.get<boolean>("includePublic") ?? true;
  const includeInternal = config.get<boolean>("includeInternal") ?? true;
  const excludeGenerated = config.get<boolean>("excludeGenerated") ?? true;

  const reportPath = path.join(path.dirname(solution.path), "dotnetprune-report.json");

  // Build command arguments
  const args = [
    findUnusedPath,
    "--output", reportPath
  ];

  if (includePublic) {
    args.push("--include-public");
  } else {
    args.push("--no-public");
  }

  if (includeInternal) {
    args.push("--include-internal");
  } else {
    args.push("--no-internal");
  }

  if (excludeGenerated) {
    args.push("--exclude-generated");
  } else {
    args.push("--no-generated");
  }

  // Add the specific solution/project path instead of workspace folder
  args.push(solution.path);

  try {
    outputChannel.appendLine(`🚀 Running analysis for ${solution.name}...`);
    outputChannel.appendLine(`Command: ${FINDUNUSED_EXE} ${args.join(" ")}`);
    
    const { stdout, stderr } = await execAsync(`${FINDUNUSED_EXE} ${args.join(" ")}`, {
      timeout: 300000 // 5 minute timeout
    });

    if (stdout) {
      outputChannel.appendLine(stdout);
    }
    if (stderr) {
      outputChannel.appendLine(`Errors: ${stderr}`);
    }

    outputChannel.appendLine("✅ Analysis completed. Loading findings...");
    
    // Refresh findings with the new report
    await refreshFindings();

    outputChannel.appendLine(`✨ Analysis complete for ${solution.name}`);
  } catch (error: any) {
    outputChannel.appendLine(`❌ Error running analysis for ${solution.name}: ${error.message}`);
    if (error.code === 'ETIMEDOUT') {
      outputChannel.appendLine("⏰ Analysis timed out (5 minutes)");
    }
  }
}

export async function activate(context: vscode.ExtensionContext) {
  diagnosticCollection =
    vscode.languages.createDiagnosticCollection("dotnetprune");
  outputChannel = vscode.window.createOutputChannel(OUTPUT_CHANNEL_NAME);

  context.subscriptions.push(diagnosticCollection, outputChannel);

  const refreshCmd = vscode.commands.registerCommand(
    "dotnetprune.refreshReport",
    async () => {
      await refreshFindings();
    }
  );

  const runAnalysisCmd = vscode.commands.registerCommand(
    "dotnetprune.runAnalysis",
    async () => {
      await runAnalysis();
    }
  );

  const openCmd = vscode.commands.registerCommand(
    "dotnetprune.openReport",
    async () => {
      const reportPath = resolveReportPath();
      if (!reportPath) {
        vscode.window.showWarningMessage(
          "DotNetPrune report path not configured and no dotnetprune-report.json found in the workspace root."
        );
        return;
      }
      const doc = await vscode.workspace.openTextDocument(reportPath);
      await vscode.window.showTextDocument(doc);
    }
  );

  context.subscriptions.push(refreshCmd, runAnalysisCmd, openCmd);

  // Auto-discover and analyze solutions
  await autoDiscoverAndAnalyzeSolutions(context);
}

export function deactivate() {
  diagnosticCollection?.clear();
  diagnosticCollection?.dispose();
  outputChannel?.dispose();
  
  // Clean up file watchers
  fileWatchers.forEach(watcher => watcher.dispose());
  fileWatchers = [];
}

function resolveReportPath(): string | undefined {
  const config = vscode.workspace.getConfiguration("dotNetPrune");
  let reportPath = config.get<string>("reportPath") ?? "";

  if (!reportPath || reportPath.trim().length === 0) {
    // look for dotnetprune-report.json in workspace folders
    const wf = vscode.workspace.workspaceFolders;
    if (!wf || wf.length === 0) {
      return undefined;
    }
      for (const folder of wf) {
        const candidate = path.join(folder.uri.fsPath, "dotnetprune-report.json");
        if (fs.existsSync(candidate)) {
          return candidate;
        }
      }
      return undefined;
    }

  // If path is workspace-relative (starts without drive or slash), resolve relative to first workspace folder
  if (!path.isAbsolute(reportPath)) {
    const wf = vscode.workspace.workspaceFolders;
    if (!wf || wf.length === 0) {
      return path.resolve(reportPath);
    }
      return path.join(wf[0].uri.fsPath, reportPath);
    }
  return reportPath;
}

function resolveFindUnusedPath(): string | undefined {
  const config = vscode.workspace.getConfiguration("dotNetPrune");
  let findUnusedPath = config.get<string>("findUnusedPath") ?? "";

  if (!findUnusedPath || findUnusedPath.trim().length === 0) {
    // Look for FindUnused.dll relative to this extension
    const extensionDir = path.join(__dirname, "..", "..", "FindUnused", "FindUnused", "bin", "Debug", "net10.0", "FindUnused.dll");
    if (fs.existsSync(extensionDir)) {
      return extensionDir;
    }
    
    // Fall back to current workspace
    const workspaceDir = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (workspaceDir) {
      const candidate = path.join(workspaceDir, "FindUnused", "FindUnused", "bin", "Debug", "net10.0", "FindUnused.dll");
      if (fs.existsSync(candidate)) {
        return candidate;
      }
    }
    
    return undefined;
  }

  // If path is workspace-relative, resolve relative to first workspace folder
  if (!path.isAbsolute(findUnusedPath)) {
    const wf = vscode.workspace.workspaceFolders;
    if (!wf || wf.length === 0) {
      return path.resolve(findUnusedPath);
    }
      return path.join(wf[0].uri.fsPath, findUnusedPath);
    }
  return findUnusedPath;
}

async function runAnalysis() {
  outputChannel.clear();
  outputChannel.show(true);

  const findUnusedPath = resolveFindUnusedPath();
  if (!findUnusedPath) {
    outputChannel.appendLine("Error: Could not find FindUnused.Console.dll");
    vscode.window.showErrorMessage(
      "DotNetPrune: Could not find FindUnused.Console.dll. Configure dotNetPrune.findUnusedPath or ensure FindUnused is built."
    );
    return;
  }

  const config = vscode.workspace.getConfiguration("dotNetPrune");
  const includePublic = config.get<boolean>("includePublic") ?? true;
  const includeInternal = config.get<boolean>("includeInternal") ?? true;
  const excludeGenerated = config.get<boolean>("excludeGenerated") ?? true;
  
  // Get workspace folder
  const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
  if (!workspaceFolder) {
    outputChannel.appendLine("Error: No workspace folder found");
    vscode.window.showErrorMessage("DotNetPrune: No workspace folder found");
    return;
  }

  const reportPath = path.join(workspaceFolder.uri.fsPath, "dotnetprune-report.json");

  // Build command arguments for the console application
  const args = [
    findUnusedPath,
    "--output", reportPath
  ];

  if (includePublic) {
    args.push("--include-public");
  } else {
    args.push("--no-public");
  }

  if (includeInternal) {
    args.push("--include-internal");
  } else {
    args.push("--no-internal");
  }

  if (excludeGenerated) {
    args.push("--exclude-generated");
  } else {
    args.push("--no-generated");
  }

  // Add workspace folder as target
  args.push(workspaceFolder.uri.fsPath);

  try {
    outputChannel.appendLine(`Running FindUnused analysis...`);
    outputChannel.appendLine(`Command: ${FINDUNUSED_EXE} ${args.join(" ")}`);
    
    const { stdout, stderr } = await execAsync(`${FINDUNUSED_EXE} ${args.join(" ")}`, {
      timeout: 300000 // 5 minute timeout
    });

    if (stdout) {
      outputChannel.appendLine(stdout);
    }
    if (stderr) {
      outputChannel.appendLine(`Errors: ${stderr}`);
    }

    outputChannel.appendLine("Analysis completed. Loading findings...");
    
    // After analysis, load the findings
    await refreshFindings();

    vscode.window.showInformationMessage("DotNetPrune: Analysis completed successfully");
  } catch (error: any) {
    outputChannel.appendLine(`Error running analysis: ${error.message}`);
    if (error.code === 'ETIMEDOUT') {
      vscode.window.showErrorMessage("DotNetPrune: Analysis timed out (5 minutes)");
    } else {
      vscode.window.showErrorMessage(`DotNetPrune: Analysis failed: ${error.message}`);
    }
  }
}

async function refreshFindings() {
  diagnosticCollection.clear();
  outputChannel.clear();
  outputChannel.show(true);

  const reportPath = resolveReportPath();
  if (!reportPath) {
    outputChannel.appendLine("No DotNetPrune report found or configured.");
    vscode.window.showInformationMessage(
      "DotNetPrune: no report found. Configure dotNetPrune.reportPath or place dotnetprune-report.json in workspace root."
    );
    return;
  }

  outputChannel.appendLine(`Loading DotNetPrune report: ${reportPath}`);
  let raw: string;
  try {
    raw = fs.readFileSync(reportPath, "utf8");
  } catch (err) {
    outputChannel.appendLine(`Failed to read report: ${err}`);
    vscode.window.showErrorMessage(
      `DotNetPrune: failed to read report: ${err}`
    );
    return;
  }

  let findings: Finding[] = [];
  try {
    findings = JSON.parse(raw) as Finding[];
  } catch (err) {
    outputChannel.appendLine(`Failed to parse report JSON: ${err}`);
    vscode.window.showErrorMessage(
      "DotNetPrune: failed to parse report JSON. Check the report format."
    );
    return;
  }

  // Optionally apply showOnlyHighConfidence if report contains confidence field (backwards-compatible)
  const config = vscode.workspace.getConfiguration("dotNetPrune");
  const onlyHigh = config.get<boolean>("showOnlyHighConfidence") ?? false;

  outputChannel.appendLine(
    `Findings loaded: ${findings.length}. Applying filters: showOnlyHighConfidence=${onlyHigh}`
  );

  // group diagnostics by file
  const diagnosticsByFile = new Map<string, vscode.Diagnostic[]>();

  for (const f of findings) {
    // optionally skip if finding contains 'confidence' and onlyHigh set (report format dependent)
    if (onlyHigh && (f as any).confidence !== undefined) {
      const conf = (f as any).confidence;
      if (conf < 0.75) {
        continue;
      }
    }

    // Map report file path to workspace file
    let filePath = f.FilePath ?? "";
    if (!path.isAbsolute(filePath)) {
      // if file path is relative, try to resolve from workspace folders (project may have provided project.Name)
      const wf = vscode.workspace.workspaceFolders;
      if (wf && wf.length > 0) {
        // try each folder
        let resolved: string | undefined;
        for (const folder of wf) {
          const candidate = path.join(folder.uri.fsPath, filePath);
          if (fs.existsSync(candidate)) {
            resolved = candidate;
            break;
          }
        }
        if (resolved) {
          filePath = resolved;
        }
      }
    }

    if (!fs.existsSync(filePath)) {
      // skip missing files but log
      outputChannel.appendLine(
        `Warning: file not found for finding: ${filePath} (symbol ${f.SymbolName})`
      );
      continue;
    }

    // Line numbers in report are 1-based; clamp to file length
    const line0 = Math.max(0, f.Line > 0 ? f.Line - 1 : 0);
    const diagnosticRange = new vscode.Range(
      new vscode.Position(line0, 0),
      new vscode.Position(line0, 400)
    );
    const message = `${f.SymbolKind} '${f.SymbolName}' (${f.Accessibility}) — ${f.Remarks}`;
    // severity heuristic: private/internal->Information, public->Warning
    const severity =
      f.Accessibility?.toLowerCase() === "public"
        ? vscode.DiagnosticSeverity.Warning
        : vscode.DiagnosticSeverity.Information;
    const diag = new vscode.Diagnostic(diagnosticRange, message, severity);
    diag.source = "DotNetPrune";
    diag.code = "dotnet-prune";

    const uri = vscode.Uri.file(filePath).toString();
    const arr = diagnosticsByFile.get(uri) ?? [];
    arr.push(diag);
    diagnosticsByFile.set(uri, arr);
  }

  // publish diagnostics
  for (const [uriStr, diags] of diagnosticsByFile) {
    const uri = vscode.Uri.parse(uriStr);
    diagnosticCollection.set(uri, diags);
  }

  outputChannel.appendLine(
    `Diagnostics published for ${diagnosticsByFile.size} files.`
  );
  vscode.window.showInformationMessage(
    `DotNetPrune: loaded ${findings.length} findings, published diagnostics for ${diagnosticsByFile.size} files.`
  );
}
