# Migration Plan

## 1. Baseline

| Aspect | Value |
|--------|-------|
| .NET Target Framework | net10.0 |
| .NET SDK | 11.0.100-preview.5.26302.115 |
| C# Language Version | preview |
| TypeScript | 7.0.2 (via package.json `^7.0.2`) |
| @types/vscode | 1.110.0 (before migration) |
| @types/node | 26.2.0 |
| VS Code Engine | ^1.110.0 |
| Node.js Runtime | v26.1.0 |
| Package Manager | pnpm |
| NuGet Packages | Microsoft.Build.Locator 1.11.2, Microsoft.CodeAnalysis.Workspaces.MSBuild 5.3.0, Microsoft.CodeAnalysis.CSharp.Workspaces 5.3.0 |

## 2. Architecture

The repository contains a VS Code extension (`dotnet-prune-vscode`) that wraps a .NET 10 console application (`FindUnused`) for analyzing unused code in .NET solutions.

- **TypeScript Extension**: Located in `src/`, bundled with esbuild to `dist/extension.js`
- **.NET Analyzer**: Located in `FindUnused/`, published as a DLL to `dist/FindUnused/`
- **IPC**: The extension spawns `dotnet FindUnused.dll <target>` and parses JSON from stdout
- **No tests**: Neither .NET nor TypeScript tests exist in the repository

## 3. Migration Phases

### Phase 1: Dependency Analysis
- Verified NuGet packages are at latest stable versions compatible with .NET 10
- Verified npm packages and updated @types/vscode to 1.125.0

### Phase 2: TypeScript 7 Migration
- TypeScript was already specified as `^7.0.2` in package.json
- After `pnpm install`, TypeScript 7.0.2 was installed
- tsconfig.json was validated for TypeScript 7 compatibility
- No breaking TypeScript 7 API changes affected the codebase

### Phase 3: Native API Modernization
- Replaced `promisify(execFile)` with native Promise-based `execFile` in `pruneExecutor.ts`
- Replaced manual stream-based SHA-256 hashing with `fs.readFile` + `crypto.createHash` in `cache.ts`
- Replaced `any` types with `unknown` and proper type guards in `extension.ts` and `export.ts`
- Removed redundant try-catch-rethrow in `extension.ts`

### Phase 4: .NET Code Modernization
- Cleaned up extraneous blank lines in `Utilities.cs`
- Fixed CLI argument parsing in `Program.cs` to support extension flags (`--exclude-public`, `--exclude-internal`, `--include-generated`, `--strict`, `--max-findings`)
- Upgraded Roslyn packages from 5.3.0 to 5.6.0
- Verified analyzer functionality with Roslyn 5.6.0 against test solution

### Phase 5: Documentation
- Created migration documentation files

## 4. Dependency Strategy

### NuGet
- Microsoft.Build.Locator 1.11.2 (latest stable, no change)
- Microsoft.CodeAnalysis.Workspaces.MSBuild 5.6.0 (upgraded from 5.3.0)
- Microsoft.CodeAnalysis.CSharp.Workspaces 5.6.0 (upgraded from 5.3.0)

### npm
- typescript: 7.0.2 (already latest stable)
- @types/vscode: 1.125.0 (upgraded from 1.110.0)
- @types/node: 26.2.0 (already latest stable)
- esbuild: 0.28.1 (already latest stable)
- @biomejs/biome: 2.5.7 (already latest stable)

## 5. Risk Areas

- VS Code engine version bumped from ^1.110.0 to ^1.125.0
- No breaking changes to extension commands, configuration keys, or IPC protocol
- .NET ↔ TypeScript communication protocol unchanged
