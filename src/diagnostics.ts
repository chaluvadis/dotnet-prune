import * as vscode from "vscode";

export interface Finding {
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
  severity?: "error" | "warning" | "information" | "hint";
  Icon: string;
}

export class DiagnosticProvider {
  private diagnosticCollection: vscode.DiagnosticCollection;

  constructor() {
    this.diagnosticCollection =
      vscode.languages.createDiagnosticCollection("dotnetprune");
  }

  updateDiagnostics(findings: Finding[]): void {
    // Clear existing diagnostics
    this.diagnosticCollection.clear();

    // Group findings by file
    const diagnosticsByFile = new Map<string, vscode.Diagnostic[]>();

    for (const finding of findings) {
      if (!finding.FilePath) continue;

      const uri = vscode.Uri.file(finding.FilePath);
      const diagnostics = diagnosticsByFile.get(finding.FilePath) || [];

      const line = Math.max(0, finding.Line - 1);
      const range = new vscode.Range(line, 0, line, 1000);

      const severity = this.getSeverity(finding);
      const message = this.formatMessage(finding);

      const diagnostic = new vscode.Diagnostic(range, message, severity);
      diagnostic.source = "DotNetPrune";
      diagnostic.code = finding.SymbolKind;

      diagnostics.push(diagnostic);
      diagnosticsByFile.set(finding.FilePath, diagnostics);
    }

    // Set diagnostics for each file
    for (const [filePath, diagnostics] of diagnosticsByFile) {
      this.diagnosticCollection.set(vscode.Uri.file(filePath), diagnostics);
    }
  }

  clear(): void {
    this.diagnosticCollection.clear();
  }

  dispose(): void {
    this.diagnosticCollection.dispose();
  }

  private getSeverity(finding: Finding): vscode.DiagnosticSeverity {
    if (finding.severity) {
      switch (finding.severity) {
        case "error":
          return vscode.DiagnosticSeverity.Error;
        case "warning":
          return vscode.DiagnosticSeverity.Warning;
        case "information":
          return vscode.DiagnosticSeverity.Information;
        case "hint":
          return vscode.DiagnosticSeverity.Hint;
      }
    }

    // Default based on accessibility and symbol kind
    if (finding.Accessibility === "public") {
      return vscode.DiagnosticSeverity.Warning;
    }

    return vscode.DiagnosticSeverity.Information;
  }

  private formatMessage(finding: Finding): string {
    const confidence =
      finding.confidence !== undefined && finding.confidence !== null
        ? ` (confidence: ${finding.confidence}%)`
        : "";
    return `Unused ${finding.SymbolKind.toLowerCase()}: ${finding.SymbolName} in ${finding.ContainingType}${confidence}`;
  }
}
