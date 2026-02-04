import * as vscode from "vscode";
import type { Finding } from "./diagnostics";

export class InlineDecorator {
  private decorationType: vscode.TextEditorDecorationType;
  private findings: Finding[] = [];
  private disposables: vscode.Disposable[] = [];

  constructor() {
    // Create decoration type for unused code
    this.decorationType = vscode.window.createTextEditorDecorationType({
      backgroundColor: new vscode.ThemeColor("editorWarning.background"),
      borderRadius: "3px",
      opacity: "0.6",
      after: {
        contentText: " ⚠ unused",
        color: new vscode.ThemeColor("editorWarning.foreground"),
        margin: "0 0 0 10px",
        fontStyle: "italic",
      },
    });

    // Listen to active editor changes
    this.disposables.push(
      vscode.window.onDidChangeActiveTextEditor((editor) => {
        if (editor) {
          this.updateDecorations(editor);
        }
      })
    );

    // Listen to document changes
    this.disposables.push(
      vscode.workspace.onDidChangeTextDocument((event) => {
        const editor = vscode.window.activeTextEditor;
        if (editor && event.document === editor.document) {
          // Debounce decoration updates
          setTimeout(() => this.updateDecorations(editor), 500);
        }
      })
    );
  }

  updateFindings(findings: Finding[]): void {
    this.findings = findings;
    this.refreshAllVisibleEditors();
  }

  private refreshAllVisibleEditors(): void {
    for (const editor of vscode.window.visibleTextEditors) {
      this.updateDecorations(editor);
    }
  }

  private updateDecorations(editor: vscode.TextEditor): void {
    const decorations: vscode.DecorationOptions[] = [];

    // Find findings for this file
    const filePath = editor.document.uri.fsPath;
    const fileFindings = this.findings.filter((f) => f.FilePath === filePath);

    for (const finding of fileFindings) {
      const line = Math.max(0, finding.Line - 1);
      
      // Skip if line is out of range
      if (line >= editor.document.lineCount) {
        continue;
      }

      const lineText = editor.document.lineAt(line).text;
      const range = new vscode.Range(line, 0, line, lineText.length);

      const decoration: vscode.DecorationOptions = {
        range,
        hoverMessage: this.createHoverMessage(finding),
      };

      decorations.push(decoration);
    }

    editor.setDecorations(this.decorationType, decorations);
  }

  private createHoverMessage(finding: Finding): vscode.MarkdownString {
    const md = new vscode.MarkdownString();
    md.isTrusted = true;

    md.appendMarkdown(`### ⚠ Unused ${finding.SymbolKind}\n\n`);
    md.appendMarkdown(`**Symbol:** \`${finding.SymbolName}\`\n\n`);
    md.appendMarkdown(`**Type:** \`${finding.ContainingType}\`\n\n`);
    md.appendMarkdown(`**Accessibility:** ${finding.Accessibility}\n\n`);

    if (finding.confidence !== undefined) {
      md.appendMarkdown(`**Confidence:** ${finding.confidence}%\n\n`);
    }

    if (finding.Remarks) {
      md.appendMarkdown(`**Details:** ${finding.Remarks}\n\n`);
    }

    md.appendMarkdown(`---\n\n`);
    md.appendMarkdown(
      `[Delete unused code](command:dotnetprune.deleteFindingByLocation?${encodeURIComponent(JSON.stringify([finding]))}) | `
    );
    md.appendMarkdown(
      `[Ignore finding](command:dotnetprune.ignoreFindingByLocation?${encodeURIComponent(JSON.stringify([finding]))})`
    );

    return md;
  }

  clear(): void {
    for (const editor of vscode.window.visibleTextEditors) {
      editor.setDecorations(this.decorationType, []);
    }
  }

  dispose(): void {
    this.decorationType.dispose();
    for (const disposable of this.disposables) {
      disposable.dispose();
    }
  }
}
