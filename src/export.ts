import * as vscode from "vscode";
import * as fs from "node:fs";
import * as path from "node:path";
import type { Finding } from "./diagnostics";

export class FindingsExporter {
  async exportFindings(findings: Finding[]): Promise<void> {
    const format = await vscode.window.showQuickPick(
      [
        { label: "JSON", value: "json" },
        { label: "CSV", value: "csv" },
        { label: "Markdown", value: "markdown" },
        { label: "HTML", value: "html" },
      ],
      { placeHolder: "Select export format" }
    );

    if (!format) return;

    const uri = await vscode.window.showSaveDialog({
      filters: {
        [format.label]: [format.value],
      },
      defaultUri: vscode.Uri.file(
        path.join(
          this.getWorkspaceRoot(),
          `dotnetprune-findings.${format.value}`
        )
      ),
    });

    if (!uri) return;

    try {
      let content: string;
      switch (format.value) {
        case "json":
          content = this.exportAsJson(findings);
          break;
        case "csv":
          content = this.exportAsCsv(findings);
          break;
        case "markdown":
          content = this.exportAsMarkdown(findings);
          break;
        case "html":
          content = this.exportAsHtml(findings);
          break;
        default:
          throw new Error("Unsupported format");
      }

      await fs.promises.writeFile(uri.fsPath, content, "utf-8");
      vscode.window.showInformationMessage(
        `DotNetPrune: Findings exported to ${uri.fsPath}`
      );

      // Ask if user wants to open the file
      const open = await vscode.window.showInformationMessage(
        "Export complete. Open file?",
        "Open",
        "Cancel"
      );
      if (open === "Open") {
        await vscode.commands.executeCommand("vscode.open", uri);
      }
    } catch (error: any) {
      vscode.window.showErrorMessage(
        `DotNetPrune: Failed to export findings: ${error.message}`
      );
    }
  }

  private exportAsJson(findings: Finding[]): string {
    return JSON.stringify(findings, null, 2);
  }

  private exportAsCsv(findings: Finding[]): string {
    const headers = [
      "Project",
      "File",
      "Line",
      "SymbolKind",
      "SymbolName",
      "ContainingType",
      "Accessibility",
      "Confidence",
      "Severity",
      "Remarks",
    ];

    const rows = findings.map((f) => [
      this.escapeCsv(f.Project),
      this.escapeCsv(f.FilePathDisplay || f.FilePath),
      f.Line.toString(),
      this.escapeCsv(f.SymbolKind),
      this.escapeCsv(f.SymbolName),
      this.escapeCsv(f.ContainingType),
      this.escapeCsv(f.Accessibility),
      f.confidence?.toString() || "",
      f.severity || "",
      this.escapeCsv(f.Remarks),
    ]);

    return [headers.join(","), ...rows.map((r) => r.join(","))].join("\n");
  }

  private escapeMarkdownCell(value: string | undefined): string {
    if (!value) return "";
    // Escape backslashes first, then pipes, then strip newlines
    return value
      .replace(/\\/g, "\\\\")
      .replace(/\|/g, "\\|")
      .replace(/[\r\n]+/g, " ");
  }

  private exportAsMarkdown(findings: Finding[]): string {
    let md = "# DotNetPrune Analysis Report\n\n";
    md += `Generated: ${new Date().toISOString()}\n\n`;
    md += `Total findings: ${findings.length}\n\n`;

    // Group by project
    const byProject = new Map<string, Finding[]>();
    for (const f of findings) {
      const project = f.Project || "Unknown";
      if (!byProject.has(project)) {
        byProject.set(project, []);
      }
      byProject.get(project)!.push(f);
    }

    for (const [project, projectFindings] of byProject) {
      md += `## ${project}\n\n`;
      md += `Findings: ${projectFindings.length}\n\n`;
      md += "| File | Line | Kind | Symbol | Type | Accessibility |\n";
      md += "|------|------|------|--------|------|---------------|\n";

      for (const f of projectFindings) {
        const cell = (v: string | undefined) => this.escapeMarkdownCell(v);
        md += `| ${cell(f.DisplayName || f.FilePathDisplay)} | ${f.Line} | ${cell(f.SymbolKind)} | ${cell(f.SymbolName)} | ${cell(f.ContainingType)} | ${cell(f.Accessibility)} |\n`;
      }

      md += "\n";
    }

    return md;
  }

  private exportAsHtml(findings: Finding[]): string {
    let html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>DotNetPrune Analysis Report</title>
  <style>
    body { font-family: Arial, sans-serif; margin: 20px; }
    h1 { color: #333; }
    h2 { color: #666; border-bottom: 2px solid #ddd; padding-bottom: 5px; }
    table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }
    th { background-color: #f2f2f2; text-align: left; padding: 10px; border: 1px solid #ddd; }
    td { padding: 8px; border: 1px solid #ddd; }
    tr:hover { background-color: #f5f5f5; }
    .summary { background-color: #e8f4f8; padding: 15px; border-radius: 5px; margin-bottom: 20px; }
    .badge { padding: 3px 8px; border-radius: 3px; font-size: 0.9em; }
    .badge-public { background-color: #ff6b6b; color: white; }
    .badge-private { background-color: #4ecdc4; color: white; }
    .badge-internal { background-color: #45b7d1; color: white; }
  </style>
</head>
<body>
  <h1>DotNetPrune Analysis Report</h1>
  <div class="summary">
    <p><strong>Generated:</strong> ${new Date().toISOString()}</p>
    <p><strong>Total findings:</strong> ${findings.length}</p>
  </div>
`;

    // Group by project
    const byProject = new Map<string, Finding[]>();
    for (const f of findings) {
      const project = f.Project || "Unknown";
      if (!byProject.has(project)) {
        byProject.set(project, []);
      }
      byProject.get(project)!.push(f);
    }

    for (const [project, projectFindings] of byProject) {
      html += `  <h2>${this.escapeHtml(project)}</h2>\n`;
      html += `  <p>Findings: ${projectFindings.length}</p>\n`;
      html += `  <table>\n`;
      html += `    <tr>
      <th>File</th>
      <th>Line</th>
      <th>Kind</th>
      <th>Symbol</th>
      <th>Type</th>
      <th>Accessibility</th>
      <th>Confidence</th>
    </tr>\n`;

      for (const f of projectFindings) {
        const accessibilityClass = `badge badge-${f.Accessibility.toLowerCase().replace(/\s+/g, "-")}`;
        html += `    <tr>
      <td>${this.escapeHtml(f.DisplayName || f.FilePathDisplay)}</td>
      <td>${f.Line}</td>
      <td>${this.escapeHtml(f.SymbolKind)}</td>
      <td>${this.escapeHtml(f.SymbolName)}</td>
      <td>${this.escapeHtml(f.ContainingType)}</td>
      <td><span class="${accessibilityClass}">${this.escapeHtml(f.Accessibility)}</span></td>
      <td>${f.confidence !== undefined && f.confidence !== null ? f.confidence : "N/A"}</td>
    </tr>\n`;
      }

      html += `  </table>\n`;
    }

    html += `</body>
</html>`;

    return html;
  }

  private escapeCsv(value: string): string {
    if (!value) return "";
    if (value.includes(",") || value.includes('"') || value.includes("\n")) {
      return `"${value.replace(/"/g, '""')}"`;
    }
    return value;
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

  private getWorkspaceRoot(): string {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) {
      return ".";
    }
    return workspaceFolders[0].uri.fsPath;
  }
}
