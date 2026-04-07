import * as vscode from "vscode";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import * as fs from "node:fs";
import * as path from "node:path";
import type { PackagePruneEntry, ProjectPrunePlan } from "./packageInventory";

const execFileAsync = promisify(execFile);

// ─── Outcome Types ────────────────────────────────────────────────────────────

export type PackagePruneOutcome = {
  packageName: string;
  version?: string;
  confidence: string;
  status: "removed" | "skipped" | "failed" | "dry-run";
  reason?: string;
  error?: string;
};

export type ProjectPruneOutcome = {
  projectName: string;
  projectPath: string;
  packages: PackagePruneOutcome[];
  restoreStatus: "success" | "failed" | "skipped";
  buildStatus: "success" | "failed" | "skipped";
};

export type PruneReport = {
  timestamp: string;
  dryRun: boolean;
  projects: ProjectPruneOutcome[];
  totalRemoved: number;
  totalSkipped: number;
  totalFailed: number;
  totalDryRun: number;
};

// ─── Executor ─────────────────────────────────────────────────────────────────

export class PruneExecutor {
  constructor(
    private readonly outputChannel: vscode.OutputChannel,
    private readonly dryRun: boolean = true
  ) {}

  private log(msg: string): void {
    this.outputChannel.appendLine(msg);
  }

  async executeProjectPlan(
    plan: ProjectPrunePlan,
    options: { runRestore: boolean; runBuild: boolean }
  ): Promise<ProjectPruneOutcome> {
    const packages: PackagePruneOutcome[] = [];

    this.log(
      `\n[${this.dryRun ? "DRY-RUN" : "APPLY"}] Project: ${plan.projectName}`
    );

    for (const entry of plan.entries) {
      const outcome = await this.processEntry(entry, plan.projectPath);
      packages.push(outcome);
    }

    let restoreStatus: "success" | "failed" | "skipped" = "skipped";
    let buildStatus: "success" | "failed" | "skipped" = "skipped";

    if (!this.dryRun) {
      if (options.runRestore) {
        restoreStatus = await this.runDotnetRestore(plan.projectPath);
      }
      if (options.runBuild) {
        buildStatus = await this.runDotnetBuild(plan.projectPath);
      }
    }

    return { projectName: plan.projectName, projectPath: plan.projectPath, packages, restoreStatus, buildStatus };
  }

  private async processEntry(
    entry: PackagePruneEntry,
    projectPath: string
  ): Promise<PackagePruneOutcome> {
    const { pkg, confidence, reason } = entry;

    // Never remove Blocked packages
    if (confidence === "Blocked") {
      this.log(`  [SKIP] ${pkg.include}: ${reason}`);
      return { packageName: pkg.include, version: pkg.version, confidence, status: "skipped", reason };
    }

    if (this.dryRun) {
      this.log(
        `  [DRY-RUN] Would remove: ${pkg.include}${pkg.version ? ` (${pkg.version})` : ""} [${confidence}]`
      );
      return { packageName: pkg.include, version: pkg.version, confidence, status: "dry-run", reason };
    }

    // Actually remove
    try {
      this.log(
        `  [REMOVE] ${pkg.include}${pkg.version ? ` (${pkg.version})` : ""} [${confidence}]`
      );
      await execFileAsync("dotnet", ["remove", projectPath, "package", pkg.include]);
      this.log(`  [OK] Removed ${pkg.include}`);
      return { packageName: pkg.include, version: pkg.version, confidence, status: "removed", reason };
    } catch (err) {
      const error = err instanceof Error ? err.message : String(err);
      this.log(`  [FAIL] ${pkg.include}: ${error}`);
      return { packageName: pkg.include, version: pkg.version, confidence, status: "failed", reason, error };
    }
  }

  private async runDotnetRestore(projectPath: string): Promise<"success" | "failed"> {
    try {
      this.log("  [RESTORE] Running dotnet restore...");
      await execFileAsync("dotnet", ["restore", projectPath]);
      this.log("  [RESTORE] Success");
      return "success";
    } catch (err) {
      const error = err instanceof Error ? err.message : String(err);
      this.log(`  [RESTORE] Failed: ${error}`);
      return "failed";
    }
  }

  private async runDotnetBuild(projectPath: string): Promise<"success" | "failed"> {
    try {
      this.log("  [BUILD] Running dotnet build...");
      await execFileAsync("dotnet", ["build", projectPath, "--no-restore"]);
      this.log("  [BUILD] Success");
      return "success";
    } catch (err) {
      const error = err instanceof Error ? err.message : String(err);
      this.log(`  [BUILD] Failed: ${error}`);
      return "failed";
    }
  }

  // ─── Report Helpers ──────────────────────────────────────────────────────────

  static buildReport(outcomes: ProjectPruneOutcome[], dryRun: boolean): PruneReport {
    let totalRemoved = 0;
    let totalSkipped = 0;
    let totalFailed = 0;
    let totalDryRun = 0;

    for (const proj of outcomes) {
      for (const pkg of proj.packages) {
        if (pkg.status === "removed") totalRemoved++;
        else if (pkg.status === "skipped") totalSkipped++;
        else if (pkg.status === "failed") totalFailed++;
        else if (pkg.status === "dry-run") totalDryRun++;
      }
    }

    return {
      timestamp: new Date().toISOString(),
      dryRun,
      projects: outcomes,
      totalRemoved,
      totalSkipped,
      totalFailed,
      totalDryRun,
    };
  }

  static async saveReport(report: PruneReport): Promise<string | undefined> {
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (!workspaceFolders || workspaceFolders.length === 0) return undefined;

    const folder = workspaceFolders[0].uri.fsPath;
    const timestamp = report.timestamp.replace(/[:.]/g, "-");
    const filename = `dotnet-prune-report-${timestamp}.json`;
    const filePath = path.join(folder, filename);

    try {
      fs.writeFileSync(filePath, JSON.stringify(report, null, 2), "utf-8");
      return filePath;
    } catch {
      return undefined;
    }
  }

  static formatReportSummary(report: PruneReport): string {
    const mode = report.dryRun ? "DRY-RUN" : "APPLIED";
    const lines: string[] = [`[${mode}] Prune Report — ${report.timestamp}`];
    for (const proj of report.projects) {
      const removed = proj.packages.filter((p) => p.status === "removed").length;
      const dryRun = proj.packages.filter((p) => p.status === "dry-run").length;
      const skipped = proj.packages.filter((p) => p.status === "skipped").length;
      const failed = proj.packages.filter((p) => p.status === "failed").length;
      lines.push(`  ${proj.projectName}: removed=${removed}, dry-run=${dryRun}, skipped=${skipped}, failed=${failed}`);
      if (proj.restoreStatus !== "skipped") lines.push(`    restore: ${proj.restoreStatus}`);
      if (proj.buildStatus !== "skipped") lines.push(`    build: ${proj.buildStatus}`);
    }
    lines.push(`Total: removed=${report.totalRemoved}, dry-run=${report.totalDryRun}, skipped=${report.totalSkipped}, failed=${report.totalFailed}`);
    return lines.join("\n");
  }
}
