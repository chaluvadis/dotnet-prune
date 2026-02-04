import * as vscode from "vscode";
import type { Finding } from "./diagnostics";

export class CodeActionsProvider implements vscode.CodeActionProvider {
  private findings: Finding[] = [];

  updateFindings(findings: Finding[]): void {
    this.findings = findings;
  }

  provideCodeActions(
    document: vscode.TextDocument,
    range: vscode.Range,
    context: vscode.CodeActionContext,
    token: vscode.CancellationToken
  ): vscode.CodeAction[] | undefined {
    const actions: vscode.CodeAction[] = [];

    // Check if there's a DotNetPrune diagnostic at this location
    const dotnetPruneDiagnostics = context.diagnostics.filter(
      (d) => d.source === "DotNetPrune"
    );

    if (dotnetPruneDiagnostics.length === 0) {
      return undefined;
    }

    // Find the corresponding finding
    const finding = this.findFindingForLocation(document.uri.fsPath, range.start.line);

    if (!finding) {
      return undefined;
    }

    // Create "Delete unused code" action
    const deleteAction = new vscode.CodeAction(
      `Delete unused ${finding.SymbolKind.toLowerCase()}: ${finding.SymbolName}`,
      vscode.CodeActionKind.QuickFix
    );
    deleteAction.command = {
      command: "dotnetprune.deleteFindingByLocation",
      title: "Delete unused code",
      arguments: [finding],
    };
    deleteAction.diagnostics = dotnetPruneDiagnostics;
    deleteAction.isPreferred = false;
    actions.push(deleteAction);

    // Create "Ignore this finding" action
    const ignoreAction = new vscode.CodeAction(
      `Ignore this finding`,
      vscode.CodeActionKind.QuickFix
    );
    ignoreAction.command = {
      command: "dotnetprune.ignoreFindingByLocation",
      title: "Ignore finding",
      arguments: [finding],
    };
    ignoreAction.diagnostics = dotnetPruneDiagnostics;
    actions.push(ignoreAction);

    // Create "Show details" action
    const detailsAction = new vscode.CodeAction(
      `Show details about this finding`,
      vscode.CodeActionKind.QuickFix
    );
    detailsAction.command = {
      command: "dotnetprune.showFindingDetails",
      title: "Show finding details",
      arguments: [finding],
    };
    actions.push(detailsAction);

    return actions;
  }

  private findFindingForLocation(filePath: string, line: number): Finding | undefined {
    // Line is 0-based in VS Code, but 1-based in findings
    const targetLine = line + 1;

    return this.findings.find(
      (f) =>
        f.FilePath === filePath &&
        Math.abs(f.Line - targetLine) <= 2 // Allow some tolerance
    );
  }
}
