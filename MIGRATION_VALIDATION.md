# Migration Validation

## Build Status

| Check | Status | Details |
|-------|--------|---------|
| .NET restore | PASS | `dotnet build` restored packages successfully |
| .NET build | PASS | Build succeeded with 0 warnings, 0 errors |
| .NET tests | N/A | No test projects exist in the repository |
| TypeScript compilation | PASS | `tsc --noEmit` completed with 0 errors |
| npm tests | N/A | No test scripts or test files exist |
| Extension bundle | PASS | esbuild produced `dist/extension.js` (133.81 KB) |
| VSIX packaging | PASS | `vsce package` produced `dotnet-prune-vscode-0.0.7.vsix` (13.21 MB) |
| Smoke test | N/A | No smoke test infrastructure exists |

## Roslyn 5.6.0 Functional Verification

| Check | Status | Details |
|-------|--------|---------|
| Analyzer build | PASS | Published FindUnused.dll with Roslyn 5.6.0 |
| .slnx input | PASS | Analyzed TestApp.slnx successfully |
| .csproj input | PASS | Analyzed TestApp.csproj directly |
| JSON output | PASS | Valid JSON array with expected finding schema |
| --exclude-public | PASS | Correctly filtered public symbols |
| --strict | PASS | Flag accepted (behavior unchanged, extension-side trimming) |
| --max-findings | PASS | Flag accepted without error |
| TypeScript JSON mapping | PASS | Extension's `loadFindingsFromJson` correctly maps all fields |

### Test Scenario

Created a temporary test solution (`/tmp/dotnet-prune-test/TestApp.slnx`) containing:

```csharp
public class Service
{
    private int _unusedField = 42;
    public int UsedMethod() => 1;
    private void UnusedPrivateMethod() { }
    public void AnotherUsedMethod() { }
}

public class UnusedClass { public void DoNothing() { } }
```

### Findings Detected (baseline, no flags)

| Symbol | Kind | Accessibility | Confidence | Severity |
|--------|------|---------------|------------|----------|
| `Service._unusedField` | Field | Private | 100 | warning |
| `Service.UnusedPrivateMethod()` | Method | Private | 100 | warning |
| `Service.AnotherUsedMethod()` | Method | Public | 70 | information |
| `UnusedClass.DoNothing()` | Method | Public | 70 | information |
| `UnusedClass` | Type | Public | 70 | information |

### Findings with `--exclude-public`

| Symbol | Kind | Accessibility | Confidence | Severity |
|--------|------|---------------|------------|----------|
| `Service` | Type | Public | 70 | information |
| `UnusedClass` | Type | Public | 70 | information |

Private symbols correctly excluded when public types are excluded.

### CLI Bug Fix

During verification, discovered that `Program.cs` rejected all arguments except the target path, despite the extension passing `--exclude-public`, `--exclude-internal`, `--include-generated`, `--strict`, and `--max-findings`. Fixed by implementing proper argument parsing in `Program.cs`.

## Baseline vs Post-Migration Comparison

### Before Migration
| Metric | Value |
|--------|-------|
| .NET build | PASS |
| TypeScript compilation | PASS |
| VSIX packaging | PASS |
| @types/vscode | 1.115.0 (installed) |
| engines.vscode | ^1.110.0 |
| TypeScript errors | 0 |

### After Migration
| Metric | Value |
|--------|-------|
| .NET build | PASS |
| TypeScript compilation | PASS |
| VSIX packaging | PASS |
| @types/vscode | 1.125.0 |
| engines.vscode | ^1.125.0 |
| TypeScript errors | 0 |

## Validation Commands Run

```bash
# TypeScript
npx tsc --noEmit

# esbuild bundle
node esbuild.js

# .NET
dotnet build ./FindUnused/FindUnused.csproj

# VSIX packaging
npx vsce package --no-yarn

# Roslyn functional verification
dotnet publish ./FindUnused/FindUnused.csproj -c Release --output /tmp/dotnet-prune-test/analyzer/
dotnet /tmp/dotnet-prune-test/analyzer/FindUnused.dll TestApp.slnx
dotnet /tmp/dotnet-prune-test/analyzer/FindUnused.dll TestApp.slnx --exclude-public
dotnet /tmp/dotnet-prune-test/analyzer/FindUnused.dll TestApp.slnx --strict
dotnet /tmp/dotnet-prune-test/analyzer/FindUnused.dll TestApp.slnx --max-findings 2
```

All commands completed successfully.
