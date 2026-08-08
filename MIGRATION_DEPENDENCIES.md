# Migration Dependencies

## NuGet Dependencies

| Package | Old Version | New Version | Action | Reason | Breaking Changes |
|---------|------------|-------------|--------|--------|-----------------|
| Microsoft.Build.Locator | 1.11.2 | 1.11.2 | Keep | Already latest stable | None |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.6.0 | 5.6.0 | Keep | Already latest stable for .NET 10 | None |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.6.0 | 5.6.0 | Keep | Already latest stable for .NET 10 | None |

## npm Dependencies

| Package | Old Version | New Version | Action | Reason | Breaking Changes |
|---------|------------|-------------|--------|--------|-----------------|
| typescript | 6.0.2 | 7.0.2 | Upgrade | Specified in package.json as `^7.0.2` | None (backward compatible) |
| @types/vscode | 1.110.0 | 1.125.0 | Upgrade | Latest stable type definitions | None (backward compatible with engine ^1.110.0) |
| @types/node | 26.2.0 | 26.2.0 | Keep | Already latest stable | None |
| esbuild | 0.28.1 | 0.28.1 | Keep | Already latest stable | None |
| @biomejs/biome | 2.5.7 | 2.5.7 | Keep | Already latest stable | None |

## Removed Dependencies

No third-party dependencies were removed. The migration replaced internal utility patterns with native platform APIs:

| Removed Pattern | Replacement | Files Affected |
|-----------------|-------------|----------------|
| `promisify(execFile)` from `node:util` | Native Promise-based `execFile()` from `node:child_process` | `src/pruneExecutor.ts` |
| Manual stream hashing (`fs.createReadStream` + event handlers) | `readFile` from `node:fs/promises` + `crypto.createHash().update().digest()` | `src/cache.ts` |
| `any` type in catch clauses and JSON parsing | `unknown` with type guards | `src/extension.ts`, `src/export.ts` |
