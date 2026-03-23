import * as assert from 'assert';
import * as vscode from 'vscode';
import { getConfig } from '../config';
import { FindingFilter } from '../filter';
import type { Finding } from '../diagnostics';

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
});

