import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";

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

const OUTPUT_CHANNEL_NAME = "DotNetPrune";

let diagnosticCollection: vscode.DiagnosticCollection;
let outputChannel: vscode.OutputChannel;

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

  context.subscriptions.push(refreshCmd, openCmd);

  // Immediately refresh when extension activates (best-effort)
  await refreshFindings();
}

export function deactivate() {
  diagnosticCollection?.clear();
  diagnosticCollection?.dispose();
  outputChannel?.dispose();
}

function resolveReportPath(): string | undefined {
  const config = vscode.workspace.getConfiguration("dotNetPrune");
  let reportPath = config.get<string>("reportPath") ?? "";

  if (!reportPath || reportPath.trim().length === 0) {
    // look for dotnetprune-report.json in workspace folders
    const wf = vscode.workspace.workspaceFolders;
    if (!wf || wf.length === 0) return undefined;
    for (const folder of wf) {
      const candidate = path.join(folder.uri.fsPath, "dotnetprune-report.json");
      if (fs.existsSync(candidate)) return candidate;
    }
    return undefined;
  }

  // If path is workspace-relative (starts without drive or slash), resolve relative to first workspace folder
  if (!path.isAbsolute(reportPath)) {
    const wf = vscode.workspace.workspaceFolders;
    if (!wf || wf.length === 0) return path.resolve(reportPath);
    return path.join(wf[0].uri.fsPath, reportPath);
  }
  return reportPath;
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
      if (conf < 0.75) continue;
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
        if (resolved) filePath = resolved;
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
      f.Accessibility?.toLowerCase() == "public"
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
