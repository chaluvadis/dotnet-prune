import * as vscode from "vscode";
import * as fs from "fs";
import * as path from "path";
import * as cp from "child_process";
import { promisify } from "util";

const exec = promisify(cp.exec);

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
	const provider = new UnusedTreeProvider();
	const treeView = vscode.window.createTreeView("dotnetprune.views.findings", {
		treeDataProvider: provider,
		showCollapseAll: true,
	});

	context.subscriptions.push(
		treeView,
		vscode.commands.registerCommand("dotnetprune.refresh", () =>
			provider.refresh(),
		),
		vscode.commands.registerCommand("dotnetprune.runAnalysis", async () => {
			await provider.runAnalysisAndRefresh();
		}),
		vscode.commands.registerCommand("dotnetprune.openReport", async () => {
			await provider.openReportFile();
		}),
		vscode.commands.registerCommand("dotnetprune.clearFindings", () =>
			provider.clear(),
		),
		vscode.commands.registerCommand(
			"dotnetprune.openFinding",
			async (item: FindingTreeItem) => {
				if (!item) return;
				await provider.openFinding(item.finding);
			},
		),
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
	private _onDidChangeTreeData: vscode.EventEmitter<
		TreeItemBase | undefined | void
	> = new vscode.EventEmitter<TreeItemBase | undefined>();
	readonly onDidChangeTreeData: vscode.Event<TreeItemBase | undefined | void> =
		this._onDidChangeTreeData.event;

	private findings: Finding[] = [];
	private groupedByProject: Map<string, Map<string, Finding[]>> = new Map();

	constructor() {}

	refresh(): void {
		this.loadReport()
			.then(() => {
				this._onDidChangeTreeData.fire();
			})
			.catch((err) => {
				vscode.window.showErrorMessage(
					`DotNetPrune: Failed to load report: ${err}`,
				);
			});
	}

	clear(): void {
		this.findings = [];
		this.groupedByProject.clear();
		this._onDidChangeTreeData.fire();
		vscode.window.showInformationMessage("DotNetPrune: findings cleared.");
	}

	async runAnalysisAndRefresh(): Promise<void> {
		const config = vscode.workspace.getConfiguration("dotNetPrune");
		const toolCmdTemplate = config.get<string>("toolCommand") ?? "";
		const reportPathSetting = config.get<string>("reportPath") ?? "";

		const workspaceFolders = vscode.workspace.workspaceFolders;
		if (!workspaceFolders || workspaceFolders.length === 0) {
			vscode.window.showErrorMessage(
				"DotNetPrune: Open a workspace before running analysis.",
			);
			return;
		}

		// discover solution/csproj files
		const slnCandidates = await vscode.workspace.findFiles(
			"**/*.slnx",
			"**/node_modules/**",
			10,
		);
		const slnCandidates2 = await vscode.workspace.findFiles(
			"**/*.sln",
			"**/node_modules/**",
			10,
		);
		const csprojCandidates = await vscode.workspace.findFiles(
			"**/*.csproj",
			"**/node_modules/**",
			20,
		);

		const allCandidates = [
			...slnCandidates,
			...slnCandidates2,
			...csprojCandidates,
		];
		if (allCandidates.length === 0) {
			vscode.window.showWarningMessage(
				"DotNetPrune: No .sln/.slnx/.csproj found in workspace. Please configure toolCommand/reportPath or add a project/solution to the workspace.",
			);
			return;
		}

		let chosen: vscode.Uri;
		if (allCandidates.length === 1) {
			chosen = allCandidates[0];
		} else {
			const picks = allCandidates.map((u) => ({
				label: path.relative(vscode.workspace.rootPath || "", u.fsPath),
				uri: u,
			}));
			const sel = await vscode.window.showQuickPick(picks, {
				placeHolder: "Select solution or project to analyze",
			});
			if (!sel) return;
			chosen = sel.uri;
		}

		// resolve report path
		const defaultReport =
			reportPathSetting && reportPathSetting.trim() !== ""
				? reportPathSetting
				: path.join(
						vscode.workspace.rootPath || ".",
						"dotnetprune-report.json",
					);

		// If user provided tool command, substitute placeholders and run it.
		if (toolCmdTemplate && toolCmdTemplate.trim().length > 0) {
			const toolCmd = toolCmdTemplate
				.replace(/\$\{solution\}/g, `"${chosen.fsPath}"`)
				.replace(/\$\{reportPath\}/g, `"${defaultReport}"`)
				.replace(
					/\$\{workspaceRoot\}/g,
					`"${vscode.workspace.rootPath || "."}"`,
				);
			const run = await vscode.window.withProgress(
				{
					location: vscode.ProgressLocation.Notification,
					title: "DotNetPrune: running analysis",
					cancellable: false,
				},
				async (progress) => {
					progress.report({ message: "Executing external tool..." });
					try {
						// Use shell execution so complex templates work.
						const { stdout, stderr } = await exec(toolCmd, {
							cwd: vscode.workspace.rootPath,
						});
						if (stderr && stderr.trim().length > 0) {
							// non-fatal: surface to output channel
							this.appendToOutput(stderr);
						}
						this.appendToOutput(stdout);
						return true;
					} catch (err: any) {
						vscode.window.showErrorMessage(
							`DotNetPrune: external tool failed: ${err.message || err}`,
						);
						this.appendToOutput(String(err));
						return false;
					}
				},
			);

			if (!run) return;
		} else {
			// No tool command configured: warn user we only read report
			const open = "Open report file";
			if (!fs.existsSync(defaultReport)) {
				const pick = await vscode.window.showWarningMessage(
					`DotNetPrune: No external tool configured and report not found at ${defaultReport}.`,
					open,
				);
				if (pick === open) {
					const uri = await vscode.window.showOpenDialog({
						canSelectFiles: true,
						openLabel: "Select dotnetprune report JSON",
					});
					if (!uri || uri.length === 0) return;
					// set chosen report path to the selected file and load
					await this.loadReportFromPath(uri[0].fsPath);
					this._onDidChangeTreeData.fire();
					return;
				}
				return;
			}
		}

		// after running (or if no tool), load the report
		await this.loadReportFromPath(defaultReport);
		this._onDidChangeTreeData.fire();
		vscode.window.showInformationMessage(
			"DotNetPrune: analysis complete and report loaded.",
		);
	}

	async openReportFile(): Promise<void> {
		const config = vscode.workspace.getConfiguration("dotNetPrune");
		const reportPathSetting = config.get<string>("reportPath") ?? "";
		const reportPath =
			reportPathSetting && reportPathSetting.trim() !== ""
				? path.isAbsolute(reportPathSetting)
					? reportPathSetting
					: path.join(vscode.workspace.rootPath || ".", reportPathSetting)
				: path.join(
						vscode.workspace.rootPath || ".",
						"dotnetprune-report.json",
					);

		if (!fs.existsSync(reportPath)) {
			vscode.window.showWarningMessage(
				`DotNetPrune: report not found at ${reportPath}`,
			);
			return;
		}
		const doc = await vscode.workspace.openTextDocument(reportPath);
		await vscode.window.showTextDocument(doc, { preview: true });
	}

	private async loadReport(): Promise<void> {
		const config = vscode.workspace.getConfiguration("dotNetPrune");
		const reportPathSetting = config.get<string>("reportPath") ?? "";
		const reportPath =
			reportPathSetting && reportPathSetting.trim() !== ""
				? path.isAbsolute(reportPathSetting)
					? reportPathSetting
					: path.join(vscode.workspace.rootPath || ".", reportPathSetting)
				: path.join(
						vscode.workspace.rootPath || ".",
						"dotnetprune-report.json",
					);

		await this.loadReportFromPath(reportPath);
	}

	private async loadReportFromPath(reportPath: string): Promise<void> {
		if (!fs.existsSync(reportPath)) {
			this.findings = [];
			this.groupedByProject.clear();
			vscode.window.showInformationMessage(
				`DotNetPrune: report not found at ${reportPath}`,
			);
			return;
		}

		let raw = "";
		try {
			raw = fs.readFileSync(reportPath, "utf8");
		} catch (err) {
			throw new Error(`Failed to read report: ${err}`);
		}

		let parsed: any;
		try {
			parsed = JSON.parse(raw);
		} catch (err) {
			throw new Error(`Failed to parse JSON: ${err}`);
		}

		if (!Array.isArray(parsed)) {
			throw new Error("Report JSON must be an array of findings.");
		}

		// Map to internal Finding type and normalize paths
		const mapped: Finding[] = parsed.map((p: any) => {
			const filePath = p.FilePath ?? p.filePath ?? "";
			const resolved = path.isAbsolute(filePath)
				? filePath
				: path.join(vscode.workspace.rootPath || ".", filePath);
			return {
				Project: p.Project ?? p.project ?? "",
				FilePath: resolved,
				Line: typeof p.Line === "number" ? p.Line : (p.line ?? 1),
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
				"DotNetPrune: finding has no file path.",
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
				vscode.TextEditorRevealType.InCenter,
			);
			// optionally set selection to the line
			editor.selection = new vscode.Selection(pos, pos);
		} catch (err: any) {
			vscode.window.showErrorMessage(
				`DotNetPrune: failed to open file ${f.FilePath}: ${err.message || err}`,
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
					vscode.TreeItemCollapsibleState.Collapsed,
				);
				return item;
			});
			// if no findings, show hint
			if (items.length === 0) {
				return Promise.resolve([
					new MessageTreeItem(
						"No findings. Run analysis or place dotnetprune-report.json in the workspace root.",
						vscode.TreeItemCollapsibleState.None,
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
				const fileLabel = path.relative(
					vscode.workspace.rootPath || "",
					filePath,
				);
				const fileItem = new FileTreeItem(
					fileLabel,
					filePath,
					vscode.TreeItemCollapsibleState.Collapsed,
				);
				fileItems.push(fileItem);
			}
			return Promise.resolve(fileItems);
		}

		if (element instanceof FileTreeItem) {
			const filePath = element.filePath;
			// find entries
			const projEntry = Array.from(this.groupedByProject.entries()).find(
				([, files]) => files.has(filePath),
			);
			if (!projEntry) return Promise.resolve([]);
			const findings = projEntry[1].get(filePath) || [];
			const items = findings.map((f) => {
				const label = `${f.SymbolKind}: ${f.SymbolName}`;
				const ti = new FindingTreeItem(
					label,
					f,
					vscode.TreeItemCollapsibleState.None,
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
		state: vscode.TreeItemCollapsibleState,
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
		state: vscode.TreeItemCollapsibleState,
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
		state: vscode.TreeItemCollapsibleState,
	) {
		super(label, state);
		this.contextValue = "finding";
		this.iconPath = new vscode.ThemeIcon("warning"); // severity-neutral; you can change based on accessibility/confidence
		// The command to open the finding is set by the provider
	}
}
