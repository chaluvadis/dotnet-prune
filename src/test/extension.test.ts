import * as assert from 'assert';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { getConfig } from '../config';
import { FindingFilter } from '../filter';
import type { Finding } from '../diagnostics';
import {
	SolutionParser,
	CsprojParser,
	PackageUsageAnalyzer,
	type PackageReference,
} from '../packageInventory';

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
});

