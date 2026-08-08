# Migration Native API Changes

## .NET API Changes

No .NET API changes were required. All code already uses modern .NET 10 / C# 12+ APIs:
- `record` types for DTOs
- `global using` directives
- Collection expressions (`[.. doc.Folders]`)
- Target-typed `new()` expressions
- `Math.Clamp` for confidence clamping
- `System.Text.Json` for serialization
- Native async/await patterns throughout

Minor cleanup:
- `Utilities.cs`: Removed extraneous blank lines (no API change)

### CLI argument parsing fix
- **Old pattern**: `if (args.Length != 1) { error; exit; }` — rejected all extension flags
- **New pattern**: Iterate args, parse `--exclude-public`, `--exclude-internal`, `--include-generated`, `--strict`, `--max-findings`, pass config to `RunAnalysisAsync`
- **Reason**: The VS Code extension passes these flags via `spawn("dotnet", cliArgs, ...)`. The previous implementation rejected them, making those settings non-functional.
- **Breaking change**: None (usage string now shows help text instead of silent rejection)
- **Files affected**: `FindUnused/Program.cs`

## TypeScript 7 API/Language Changes

### catch clause typing
- **Old pattern**: `catch (error: any) { ... error.message ... }`
- **New pattern**: `catch (error) { const message = error instanceof Error ? error.message : String(error); ... }`
- **Reason**: TypeScript 7 with `strict: true` discourages `any` typing in catch clauses. Using `unknown` with a type guard is safer.
- **Breaking change**: None (behavior unchanged)
- **Files affected**: `src/extension.ts`, `src/export.ts`

### JSON.parse result typing
- **Old pattern**: `interface AnalysisResponse { findings: any[]; ... }`
- **New pattern**: `interface AnalysisResponse { findings: unknown[]; ... }`
- **Reason**: Avoids implicit `any` in parsed JSON structures
- **Breaking change**: None
- **Files affected**: `src/extension.ts`

### Redundant try-catch-rethrow removal
- **Old pattern**: `try { ... } catch (error) { throw error; }`
- **New pattern**: Direct return without try-catch
- **Reason**: Eliminates unnecessary control flow
- **Breaking change**: None
- **Files affected**: `src/extension.ts` (`getDllPath`)

## Node.js Native API Changes

### child_process.execFile
- **Old API**: `promisify(execFile)` from `node:util`
- **New API**: `execFile()` directly from `node:child_process` (native Promise support since Node 18+)
- **Reason**: Node.js 18+ supports Promise-based `execFile` natively. Removes the `node:util/promisify` dependency for this use case.
- **Breaking change**: None
- **Files affected**: `src/pruneExecutor.ts`

### crypto file hashing
- **Old API**: Manual `fs.createReadStream` + `crypto.createHash` with stream event handlers
- **New API**: `readFile` from `node:fs/promises` + `crypto.createHash().update(data).digest("hex")`
- **Reason**: Simpler, more readable code using modern `fs/promises` API. For typical .cs file sizes, reading the entire file is efficient.
- **Breaking change**: None
- **Files affected**: `src/cache.ts`

## VS Code Extension API Changes

No VS Code API changes were required. The extension already uses modern VS Code Extension APIs:
- `vscode.workspace.isTrusted` (1.57+)
- `vscode.env.clipboard` (1.75+)
- `vscode.TreeDataProvider` pattern
- `vscode.CodeActionProvider` with `providedCodeActionKinds`
- `vscode.TextEditorDecorationType` with hover messages
- `vscode.ProgressLocation.Notification` with cancellable progress

## Configuration Changes

### tsconfig.json
- No structural changes required. The existing configuration is compatible with TypeScript 7:
  - `"module": "nodenext"` with `"target": "ES2022"` is valid in TypeScript 7
  - `strict: true` is enforced
  - `noUnusedParameters: true` is enforced
  - `esModuleInterop: true` is maintained

### package.json
- `engines.vscode` updated from `^1.110.0` to `^1.125.0` to match @types/vscode 1.125.0
- `@types/vscode` updated from `^1.110.0` to `^1.125.0`

### .csproj
- No changes required. `net10.0` target framework remains current.
