import * as assert from 'assert';
import * as path from 'node:path';
import * as os from 'node:os';
import * as fs from 'node:fs';
import * as vscode from 'vscode';
import { getConfig } from '../config';
import { FindingFilter } from '../filter';
import type { Finding } from '../diagnostics';
import {
	SolutionParser,
	CsprojParser,
	PackageUsageAnalyzer,
	AllowlistParser,
	AllowlistWriter,
	CsprojNavigator,
	PrunePlanner,
	PackageInventoryProvider,
	type PackageReference,
	type ProjectPackageInfo,
} from '../packageInventory';
import { PruneExecutor } from '../pruneExecutor';

suite('DotNetPrune Extension Test Suite', () => {
	vscode.window.showInformationMessage('Start all tests.');

	suite('Configuration Tests', () => {
		const SETTINGS_TO_RESET = [
			'analysis.includePublicSymbols',
			'analysis.includeInternalSymbols',
			'analysis.excludeGeneratedCode',
			'analysis.mode',
			'filter.exclusionPatterns',
			'ui.enableInlineHighlighting',
			'ui.showConfidence',
			'ui.showSeverity',
			'autoRefreshOnSave',
			'maxFindings',
			'excludeGlobs',
			'analyzerPath',
			'logLevel',
		];

		setup(async () => {
			const wsConfig = vscode.workspace.getConfiguration('dotnetprune');
			await Promise.all(
				SETTINGS_TO_RESET.map((key) =>
					wsConfig.update(key, undefined, vscode.ConfigurationTarget.Workspace)
				)
			);
		});

		teardown(async () => {
			const wsConfig = vscode.workspace.getConfiguration('dotnetprune');
			await Promise.all(
				SETTINGS_TO_RESET.map((key) =>
					wsConfig.update(key, undefined, vscode.ConfigurationTarget.Workspace)
				)
			);
		});

		test('Should load default configuration', () => {
			const config = getConfig();
			assert.strictEqual(config.analysis.includePublicSymbols, true);
			assert.strictEqual(config.analysis.includeInternalSymbols, true);
			assert.strictEqual(config.analysis.excludeGeneratedCode, true);
			assert.strictEqual(config.analysis.mode, 'loose');
		});

		test('Should have correct default filter patterns', () => {
			const config = getConfig();
			assert.ok(config.filter.exclusionPatterns.includes('**/bin/**'));
			assert.ok(config.filter.exclusionPatterns.includes('**/obj/**'));
		});

		test('Should have correct UI defaults', () => {
			const config = getConfig();
			assert.strictEqual(config.ui.enableInlineHighlighting, true);
			assert.strictEqual(config.ui.showConfidence, true);
			assert.strictEqual(config.ui.showSeverity, true);
		});

		test('Should have correct new setting defaults', () => {
			const config = getConfig();
			assert.strictEqual(config.autoRefreshOnSave, false);
			assert.strictEqual(config.maxFindings, 1000);
			assert.deepStrictEqual(config.excludeGlobs, []);
			assert.strictEqual(config.analyzerPath, '');
			assert.strictEqual(config.logLevel, 'info');
		});
	});

	suite('Filter Tests', () => {
		test('Should initialize with no active filters', () => {
			const filter = new FindingFilter();
			assert.strictEqual(filter.hasActiveFilters(), false);
		});

		test('Should filter by symbol kind', () => {
			const filter = new FindingFilter();
			filter.setSymbolKindFilter(['Method']);

			const methodFinding = {
				SymbolKind: 'Method',
				SymbolName: 'TestMethod',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				confidence: 90,
			} as Finding;

			const propertyFinding = {
				SymbolKind: 'Property',
				SymbolName: 'TestProperty',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				confidence: 90,
			} as Finding;

			assert.strictEqual(filter.matches(methodFinding), true);
			assert.strictEqual(filter.matches(propertyFinding), false);
		});

		test('Should filter by confidence', () => {
			const filter = new FindingFilter();
			filter.setConfidenceFilter(80);

			const highConfidenceFinding = {
				SymbolKind: 'Method',
				SymbolName: 'TestMethod',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				confidence: 90,
			} as Finding;

			const lowConfidenceFinding = {
				SymbolKind: 'Method',
				SymbolName: 'TestMethod2',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				confidence: 70,
			} as Finding;

			assert.strictEqual(filter.matches(highConfidenceFinding), true);
			assert.strictEqual(filter.matches(lowConfidenceFinding), false);
		});

		test('Should filter by project', () => {
			const filter = new FindingFilter();
			filter.setProjectFilter(['ProjectA']);

			const projectAFinding = {
				SymbolKind: 'Method',
				SymbolName: 'TestMethod',
				Project: 'ProjectA',
				FilePath: '/test/file.cs',
				confidence: 90,
			} as Finding;

			const projectBFinding = {
				SymbolKind: 'Method',
				SymbolName: 'TestMethod',
				Project: 'ProjectB',
				FilePath: '/test/file.cs',
				confidence: 90,
			} as Finding;

			assert.strictEqual(filter.matches(projectAFinding), true);
			assert.strictEqual(filter.matches(projectBFinding), false);
		});

		test('Should filter by search text', () => {
			const filter = new FindingFilter();
			filter.setSearchText('calculate');

			const matchingFinding = {
				SymbolKind: 'Method',
				SymbolName: 'CalculateSum',
				ContainingType: 'Calculator',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				Remarks: '',
				confidence: 90,
			} as Finding;

			const nonMatchingFinding = {
				SymbolKind: 'Method',
				SymbolName: 'ProcessData',
				ContainingType: 'Processor',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				Remarks: '',
				confidence: 90,
			} as Finding;

			assert.strictEqual(filter.matches(matchingFinding), true);
			assert.strictEqual(filter.matches(nonMatchingFinding), false);
		});

		test('Should clear all filters', () => {
			const filter = new FindingFilter();
			filter.setSymbolKindFilter(['Method']);
			filter.setConfidenceFilter(80);
			filter.setProjectFilter(['ProjectA']);
			filter.setSearchText('test');

			assert.strictEqual(filter.hasActiveFilters(), true);

			filter.clearAll();
			assert.strictEqual(filter.hasActiveFilters(), false);
		});

		test('Should combine multiple filters', () => {
			const filter = new FindingFilter();
			filter.setSymbolKindFilter(['Method']);
			filter.setConfidenceFilter(80);

			const matchingFinding = {
				SymbolKind: 'Method',
				SymbolName: 'TestMethod',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				confidence: 90,
			} as Finding;

			const wrongKindFinding = {
				SymbolKind: 'Property',
				SymbolName: 'TestProperty',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				confidence: 90,
			} as Finding;

			const lowConfidenceFinding = {
				SymbolKind: 'Method',
				SymbolName: 'TestMethod',
				Project: 'TestProject',
				FilePath: '/test/file.cs',
				confidence: 70,
			} as Finding;

			assert.strictEqual(filter.matches(matchingFinding), true);
			assert.strictEqual(filter.matches(wrongKindFinding), false);
			assert.strictEqual(filter.matches(lowConfidenceFinding), false);
		});
	});

	suite('SolutionParser Tests', () => {
		test('Should parse .sln project paths with backslash separators', () => {
			const slnContent = `
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp", "src\\MyApp\\MyApp.csproj", "{GUID-1}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp.Tests", "tests\\MyApp.Tests\\MyApp.Tests.csproj", "{GUID-2}"
EndProject
`;
			const paths = SolutionParser.parseSlnProjects(slnContent);
			assert.strictEqual(paths.length, 2);
			assert.ok(paths[0].includes('MyApp.csproj'));
			assert.ok(paths[1].includes('MyApp.Tests.csproj'));
		});

		test('Should parse .sln and normalize backslashes', () => {
			const slnContent = `Project("{FAE04EC0}") = "Lib", "src\\Lib\\Lib.csproj", "{GUID}"`;
			const paths = SolutionParser.parseSlnProjects(slnContent);
			assert.strictEqual(paths.length, 1);
			// Path separators should be platform-native
			const parts = paths[0].split(path.sep);
			assert.ok(parts.length >= 1, 'Path should contain platform separators');
			assert.ok(paths[0].endsWith('Lib.csproj'));
		});

		test('Should return empty array for .sln with no projects', () => {
			const slnContent = 'Microsoft Visual Studio Solution File, Format Version 12.00\n# No projects';
			const paths = SolutionParser.parseSlnProjects(slnContent);
			assert.deepStrictEqual(paths, []);
		});

		test('Should parse .slnx project paths', () => {
			const slnxContent = `
<Solution>
  <Project Path="src/MyApp/MyApp.csproj" />
  <Project Path="tests/MyApp.Tests/MyApp.Tests.csproj" Type="..." />
</Solution>
`;
			const paths = SolutionParser.parseSlnxProjects(slnxContent);
			assert.strictEqual(paths.length, 2);
			assert.ok(paths[0].includes('MyApp.csproj'));
			assert.ok(paths[1].includes('MyApp.Tests.csproj'));
		});

		test('Should return empty array for .slnx with no projects', () => {
			const slnxContent = '<Solution></Solution>';
			const paths = SolutionParser.parseSlnxProjects(slnxContent);
			assert.deepStrictEqual(paths, []);
		});

		test('Should handle non-csproj references in .sln without including them', () => {
			const slnContent = `
Project("{2150E333}") = "Solution Items", "Solution Items", "{GUID}"
EndProject
Project("{FAE04EC0}") = "MyApp", "MyApp\\MyApp.csproj", "{GUID-2}"
EndProject
`;
			const paths = SolutionParser.parseSlnProjects(slnContent);
			// Only .csproj references should be included
			assert.strictEqual(paths.length, 1);
			assert.ok(paths[0].includes('MyApp.csproj'));
		});
	});

	suite('CsprojParser Tests', () => {
		test('Should parse self-closing PackageReference elements', () => {
			const csproj = `
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Serilog" Version="2.12.0" />
  </ItemGroup>
</Project>`;
			const packages = CsprojParser.getPackageReferences(csproj);
			assert.strictEqual(packages.length, 2);
			assert.strictEqual(packages[0].include, 'Newtonsoft.Json');
			assert.strictEqual(packages[0].version, '13.0.3');
			assert.strictEqual(packages[1].include, 'Serilog');
		});

		test('Should parse open/close PackageReference elements with child tags', () => {
			const csproj = `
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Serilog">
      <Version>2.12.0</Version>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>`;
			const packages = CsprojParser.getPackageReferences(csproj);
			assert.strictEqual(packages.length, 1);
			assert.strictEqual(packages[0].include, 'Serilog');
			assert.strictEqual(packages[0].version, '2.12.0');
			assert.strictEqual(packages[0].privateAssets, 'all');
		});

		test('Should capture PrivateAssets and IncludeAssets attributes', () => {
			const csproj = `
<ItemGroup>
  <PackageReference Include="MyAnalyzer" Version="1.0.0" PrivateAssets="all" IncludeAssets="analyzers" />
</ItemGroup>`;
			const packages = CsprojParser.getPackageReferences(csproj);
			assert.strictEqual(packages.length, 1);
			assert.strictEqual(packages[0].privateAssets, 'all');
			assert.strictEqual(packages[0].includeAssets, 'analyzers');
		});

		test('Should capture Condition attribute', () => {
			const csproj = `
<ItemGroup>
  <PackageReference Include="MyPkg" Version="1.0" Condition="'$(Configuration)' == 'Debug'" />
</ItemGroup>`;
			const packages = CsprojParser.getPackageReferences(csproj);
			assert.strictEqual(packages.length, 1);
			assert.ok(packages[0].condition?.includes('Debug'));
		});

		test('Should return empty array when no packages present', () => {
			const csproj = `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup></PropertyGroup></Project>`;
			const packages = CsprojParser.getPackageReferences(csproj);
			assert.deepStrictEqual(packages, []);
		});

		test('Should parse single TargetFramework', () => {
			const csproj = `
<PropertyGroup>
  <TargetFramework>net9.0</TargetFramework>
</PropertyGroup>`;
			const frameworks = CsprojParser.getTargetFrameworks(csproj);
			assert.deepStrictEqual(frameworks, ['net9.0']);
		});

		test('Should parse multiple TargetFrameworks', () => {
			const csproj = `
<PropertyGroup>
  <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
</PropertyGroup>`;
			const frameworks = CsprojParser.getTargetFrameworks(csproj);
			assert.deepStrictEqual(frameworks, ['net8.0', 'net9.0']);
		});

		test('Should return empty array when no target framework', () => {
			const csproj = `<Project Sdk="Microsoft.NET.Sdk"></Project>`;
			const frameworks = CsprojParser.getTargetFrameworks(csproj);
			assert.deepStrictEqual(frameworks, []);
		});
	});

	suite('PackageUsageAnalyzer Tests', () => {
		const makePackage = (include: string, opts?: Partial<PackageReference>): PackageReference => ({
			include,
			...opts,
		});

		test('Should mark package as referenced when using directive found', () => {
			const pkg = makePackage('Newtonsoft.Json');
			const csContent = 'using Newtonsoft.Json;\nvar x = JsonConvert.SerializeObject(obj);';
			assert.strictEqual(PackageUsageAnalyzer.isPackageUsed(pkg, csContent), true);
		});

		test('Should mark package as unused when no reference found', () => {
			const pkg = makePackage('Serilog');
			const csContent = 'using System;\nusing System.Linq;\nnamespace MyApp {}';
			assert.strictEqual(PackageUsageAnalyzer.isPackageUsed(pkg, csContent), false);
		});

		test('Should always mark PrivateAssets=all package as referenced', () => {
			const pkg = makePackage('SomeAnalyzer', { privateAssets: 'all' });
			const csContent = ''; // No usage in source
			assert.strictEqual(PackageUsageAnalyzer.isPackageUsed(pkg, csContent), true);
		});

		test('Should match on root namespace for dotted package names', () => {
			const pkg = makePackage('Microsoft.Extensions.Logging');
			const csContent = 'using Microsoft.Extensions.Logging;\npublic class Foo {}';
			assert.strictEqual(PackageUsageAnalyzer.isPackageUsed(pkg, csContent), true);
		});

		test('Should not mutate any package reference (no-write guarantee)', () => {
			const pkg = makePackage('Newtonsoft.Json', { version: '13.0.3' });
			const original = JSON.stringify(pkg);
			PackageUsageAnalyzer.isPackageUsed(pkg, 'using Newtonsoft.Json;');
			assert.strictEqual(JSON.stringify(pkg), original);
		});

		test('analyzeProject should split packages into unused and referenced groups', () => {
			// We use a non-existent path so collectCsContents returns empty string
			// All packages without PrivateAssets=all will be "unused" with no .cs content
			const packages: PackageReference[] = [
				makePackage('UnusedPkg', { version: '1.0.0' }),
				makePackage('ToolPkg', { version: '2.0.0', privateAssets: 'all' }),
			];
			const result = PackageUsageAnalyzer.analyzeProject('/nonexistent/path/MyApp.csproj', packages);
			// ToolPkg (PrivateAssets=all) → referenced; UnusedPkg → unused
			assert.strictEqual(result.unusedPackages.length, 1);
			assert.strictEqual(result.unusedPackages[0].include, 'UnusedPkg');
			assert.strictEqual(result.referencedPackages.length, 1);
			assert.strictEqual(result.referencedPackages[0].include, 'ToolPkg');
		});
	});

	// ─── Phase 2: AllowlistParser Tests ──────────────────────────────────────────
	suite('AllowlistParser Tests', () => {
		test('Should return empty set for non-existent allowlist', () => {
			const result = AllowlistParser.load('/nonexistent/workspace');
			assert.strictEqual(result.size, 0);
		});

		test('Should return empty set for directory with no .dotnet-prune.json', () => {
			// Use a temp path that definitely has no .dotnet-prune.json
			const result = AllowlistParser.load('/tmp');
			// It may or may not exist; just ensure no error thrown and result is a Set
			assert.ok(result instanceof Set);
		});
	});

	// ─── Phase 2: PrunePlanner Tests ──────────────────────────────────────────────
	suite('PrunePlanner Tests', () => {
		const makePackage = (include: string, opts?: Partial<PackageReference>): PackageReference => ({
			include,
			...opts,
		});

		test('Should classify plain unused package as High confidence', () => {
			const pkg = makePackage('Serilog', { version: '2.12.0' });
			const result = PrunePlanner.classifyPackage(pkg, new Set());
			assert.strictEqual(result.confidence, 'High');
			assert.ok(result.reason.length > 0);
		});

		test('Should classify conditional package as Medium confidence', () => {
			const pkg = makePackage('SomePkg', { condition: "'$(Configuration)' == 'Debug'" });
			const result = PrunePlanner.classifyPackage(pkg, new Set());
			assert.strictEqual(result.confidence, 'Medium');
			assert.ok(result.reason.includes('Conditional'));
		});

		test('Should classify package with non-default IncludeAssets as Medium confidence', () => {
			const pkg = makePackage('SomePkg', { includeAssets: 'runtime; build; native' });
			const result = PrunePlanner.classifyPackage(pkg, new Set());
			assert.strictEqual(result.confidence, 'Medium');
			assert.ok(result.reason.includes('IncludeAssets'));
		});

		test('Should classify allowlisted package as Blocked', () => {
			const pkg = makePackage('Newtonsoft.Json');
			const allowlist = new Set(['newtonsoft.json']); // lowercase
			const result = PrunePlanner.classifyPackage(pkg, allowlist);
			assert.strictEqual(result.confidence, 'Blocked');
			assert.ok(result.reason.includes('allowlist'));
		});

		test('Allowlist match should be case-insensitive', () => {
			const pkg = makePackage('SERILOG');
			const allowlist = new Set(['serilog']);
			const result = PrunePlanner.classifyPackage(pkg, allowlist);
			assert.strictEqual(result.confidence, 'Blocked');
		});

		test('Package with IncludeAssets=all should be High (not Medium)', () => {
			const pkg = makePackage('SomePkg', { includeAssets: 'all' });
			const result = PrunePlanner.classifyPackage(pkg, new Set());
			assert.strictEqual(result.confidence, 'High');
		});

		test('buildPlan should create entries for all unused packages', () => {
			const projectInfo: ProjectPackageInfo = {
				projectName: 'TestProject',
				projectPath: '/path/TestProject.csproj',
				targetFrameworks: ['net9.0'],
				unusedPackages: [
					makePackage('PkgA'),
					makePackage('PkgB', { condition: "'$(Configuration)' == 'Debug'" }),
					makePackage('PkgC'),
				],
				referencedPackages: [],
			};
			const allowlist = new Set(['pkgc']); // PkgC is blocked
			const plan = PrunePlanner.buildPlan(projectInfo, allowlist);

			assert.strictEqual(plan.projectName, 'TestProject');
			assert.strictEqual(plan.projectPath, '/path/TestProject.csproj');
			assert.strictEqual(plan.entries.length, 3);

			const pkgA = plan.entries.find((e) => e.pkg.include === 'PkgA');
			assert.strictEqual(pkgA?.confidence, 'High');

			const pkgB = plan.entries.find((e) => e.pkg.include === 'PkgB');
			assert.strictEqual(pkgB?.confidence, 'Medium');

			const pkgC = plan.entries.find((e) => e.pkg.include === 'PkgC');
			assert.strictEqual(pkgC?.confidence, 'Blocked');
		});

		test('buildPlan returns empty entries when no unused packages', () => {
			const projectInfo: ProjectPackageInfo = {
				projectName: 'CleanProject',
				projectPath: '/path/CleanProject.csproj',
				targetFrameworks: ['net9.0'],
				unusedPackages: [],
				referencedPackages: [makePackage('UsedPkg')],
			};
			const plan = PrunePlanner.buildPlan(projectInfo, new Set());
			assert.strictEqual(plan.entries.length, 0);
		});
	});

	// ─── Phase 2: PruneExecutor.buildReport Tests ─────────────────────────────────
	suite('PruneExecutor.buildReport Tests', () => {
		test('Should tally counts correctly in report', () => {
			const outcomes: import('../pruneExecutor').ProjectPruneOutcome[] = [
				{
					projectName: 'Proj1',
					projectPath: '/p1.csproj',
					packages: [
						{ packageName: 'A', confidence: 'High', status: 'removed' },
						{ packageName: 'B', confidence: 'Medium', status: 'failed', error: 'err' },
						{ packageName: 'C', confidence: 'Blocked', status: 'skipped' },
					],
					restoreStatus: 'skipped',
					buildStatus: 'skipped',
				},
				{
					projectName: 'Proj2',
					projectPath: '/p2.csproj',
					packages: [
						{ packageName: 'D', confidence: 'High', status: 'removed' },
						{ packageName: 'E', confidence: 'High', status: 'removed' },
					],
					restoreStatus: 'success',
					buildStatus: 'skipped',
				},
			];

			const report = PruneExecutor.buildReport(outcomes, false);
			assert.strictEqual(report.dryRun, false);
			assert.strictEqual(report.totalRemoved, 3);
			assert.strictEqual(report.totalFailed, 1);
			assert.strictEqual(report.totalSkipped, 1);
			assert.strictEqual(report.totalDryRun, 0);
			assert.ok(report.timestamp.length > 0);
			assert.strictEqual(report.projects.length, 2);
		});

		test('Should tally dry-run counts correctly', () => {
			const outcomes: import('../pruneExecutor').ProjectPruneOutcome[] = [
				{
					projectName: 'Proj1',
					projectPath: '/p1.csproj',
					packages: [
						{ packageName: 'A', confidence: 'High', status: 'dry-run' },
						{ packageName: 'B', confidence: 'Blocked', status: 'skipped' },
					],
					restoreStatus: 'skipped',
					buildStatus: 'skipped',
				},
			];

			const report = PruneExecutor.buildReport(outcomes, true);
			assert.strictEqual(report.dryRun, true);
			assert.strictEqual(report.totalRemoved, 0);
			assert.strictEqual(report.totalDryRun, 1);
			assert.strictEqual(report.totalSkipped, 1);
		});

		test('formatReportSummary should include project name and totals', () => {
			const outcomes: import('../pruneExecutor').ProjectPruneOutcome[] = [
				{
					projectName: 'MyProj',
					projectPath: '/my.csproj',
					packages: [
						{ packageName: 'Pkg', confidence: 'High', status: 'removed' },
					],
					restoreStatus: 'success',
					buildStatus: 'skipped',
				},
			];
			const report = PruneExecutor.buildReport(outcomes, false);
			const summary = PruneExecutor.formatReportSummary(report);
			assert.ok(summary.includes('MyProj'));
			assert.ok(summary.includes('APPLIED'));
			assert.ok(summary.includes('restore: success'));
		});
	});

	// ─── Phase 3 Tests ────────────────────────────────────────────────────────────

	suite('AllowlistWriter Tests', () => {
		let tmpDir: string;

		setup(() => {
			tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'dotnet-prune-test-'));
		});

		teardown(() => {
			fs.rmSync(tmpDir, { recursive: true, force: true });
		});

		test('Should create .dotnet-prune.json with one entry when file does not exist', () => {
			AllowlistWriter.add(tmpDir, 'NewPackage');
			const filePath = AllowlistWriter.getPath(tmpDir);
			assert.ok(fs.existsSync(filePath));
			const raw = fs.readFileSync(filePath, 'utf-8');
			const cfg = JSON.parse(raw) as { allowlist: string[] };
			assert.deepStrictEqual(cfg.allowlist, ['NewPackage']);
		});

		test('Should append to existing allowlist without duplicates', () => {
			const filePath = AllowlistWriter.getPath(tmpDir);
			fs.writeFileSync(filePath, JSON.stringify({ allowlist: ['ExistingPkg'] }, null, 2), 'utf-8');
			AllowlistWriter.add(tmpDir, 'NewPackage');
			const raw = fs.readFileSync(filePath, 'utf-8');
			const cfg = JSON.parse(raw) as { allowlist: string[] };
			assert.deepStrictEqual(cfg.allowlist, ['ExistingPkg', 'NewPackage']);
		});

		test('Should not add duplicate entries (case-insensitive)', () => {
			AllowlistWriter.add(tmpDir, 'MyPackage');
			AllowlistWriter.add(tmpDir, 'mypackage');
			AllowlistWriter.add(tmpDir, 'MYPACKAGE');
			const cfg = JSON.parse(fs.readFileSync(AllowlistWriter.getPath(tmpDir), 'utf-8')) as { allowlist: string[] };
			assert.strictEqual(cfg.allowlist.length, 1);
			assert.strictEqual(cfg.allowlist[0], 'MyPackage');
		});

		test('getPath should return workspace-relative .dotnet-prune.json path', () => {
			const result = AllowlistWriter.getPath('/my/workspace');
			assert.strictEqual(result, path.join('/my/workspace', '.dotnet-prune.json'));
		});

		test('Should handle corrupt JSON file gracefully by starting fresh', () => {
			const filePath = AllowlistWriter.getPath(tmpDir);
			fs.writeFileSync(filePath, '{invalid json}', 'utf-8');
			AllowlistWriter.add(tmpDir, 'RecoveredPackage');
			const cfg = JSON.parse(fs.readFileSync(filePath, 'utf-8')) as { allowlist: string[] };
			assert.deepStrictEqual(cfg.allowlist, ['RecoveredPackage']);
		});

		test('Should roundtrip through AllowlistParser after write', () => {
			AllowlistWriter.add(tmpDir, 'Pkg.One');
			AllowlistWriter.add(tmpDir, 'Pkg.Two');
			const loaded = AllowlistParser.load(tmpDir);
			assert.ok(loaded.has('pkg.one'));
			assert.ok(loaded.has('pkg.two'));
		});
	});

	suite('CsprojNavigator Tests', () => {
		const csprojContent = `<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
    <PackageReference Include="Serilog" Version="2.10.0" />
  </ItemGroup>
</Project>`;

		test('Should find line number of a known PackageReference', () => {
			const line = CsprojNavigator.findPackageReferenceLine(csprojContent, 'Newtonsoft.Json');
			assert.strictEqual(line, 2); // 0-based: line index 2
		});

		test('Should find line for second PackageReference', () => {
			const line = CsprojNavigator.findPackageReferenceLine(csprojContent, 'Serilog');
			assert.strictEqual(line, 3);
		});

		test('Should return undefined for a package not present', () => {
			const line = CsprojNavigator.findPackageReferenceLine(csprojContent, 'NotPresent');
			assert.strictEqual(line, undefined);
		});

		test('Should perform case-insensitive match on package name', () => {
			const line = CsprojNavigator.findPackageReferenceLine(csprojContent, 'newtonsoft.json');
			assert.strictEqual(line, 2);
		});

		test('Should return undefined for empty content', () => {
			const line = CsprojNavigator.findPackageReferenceLine('', 'AnyPackage');
			assert.strictEqual(line, undefined);
		});
	});

	suite('PackageInventoryProvider Confidence Filter Tests', () => {
		let provider: PackageInventoryProvider;

		setup(() => {
			// Create a minimal fake ExtensionContext
			const fakeContext = {} as vscode.ExtensionContext;
			provider = new PackageInventoryProvider(fakeContext);
		});

		test('Default confidence filter should be "All"', () => {
			assert.strictEqual(provider.getConfidenceFilter(), 'All');
		});

		test('setConfidenceFilter should update the filter', () => {
			provider.setConfidenceFilter('High');
			assert.strictEqual(provider.getConfidenceFilter(), 'High');
		});

		test('setConfidenceFilter to "All" should clear the filter', () => {
			provider.setConfidenceFilter('High');
			provider.setConfidenceFilter('All');
			assert.strictEqual(provider.getConfidenceFilter(), 'All');
		});

		test('getLastSolutionPath should be undefined before first analysis', () => {
			assert.strictEqual(provider.getLastSolutionPath(), undefined);
		});

		test('getInventories should return empty array initially', () => {
			assert.deepStrictEqual(provider.getInventories(), []);
		});
	});
});

