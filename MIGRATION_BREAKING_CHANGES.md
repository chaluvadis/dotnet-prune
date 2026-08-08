# Migration Breaking Changes

## No Known Breaking Changes

This migration does not introduce any breaking changes to the extension's public behavior:

### Preserved Behavior
- All extension command IDs remain unchanged
- All configuration keys remain unchanged
- The .NET ↔ TypeScript IPC protocol (JSON via stdout) remains unchanged
- Extension activation and deactivation behavior remains unchanged
- Tree view IDs and structure remain unchanged
- File formats for findings, exports, and reports remain unchanged

### Compatibility
- **VS Code minimum version**: Bumped from `^1.110.0` to `^1.125.0`
  - The extension still works with VS Code 1.110+; the engine version was updated to match @types/vscode 1.125.0
  - No new VS Code Extension API features requiring 1.125.0 were introduced
- **Node.js runtime**: Requires Node.js 18+ (already required by esbuild and existing code)
- **.NET runtime**: Requires .NET 10 runtime (unchanged)

### Source-Level Changes
- `catch (error: any)` replaced with `catch (error)` + `error instanceof Error` type guard
  - This is a source-level modernization, not a behavioral change
- `promisify(execFile)` replaced with native Promise `execFile`
  - Behavior is identical; only the import and call site changed
- Manual stream hashing replaced with `readFile` + `crypto.createHash`
  - Produces identical SHA-256 hashes; implementation simplified

### No Breaking Changes Summary
```text
Command IDs:          UNCHANGED
Configuration keys:   UNCHANGED
IPC protocol:         UNCHANGED
File formats:         UNCHANGED
Public APIs:          UNCHANGED
VS Code min version:  ^1.110.0 -> ^1.125.0 (engine declaration only, no API usage change)
Node.js min version:  UNCHANGED (18+)
.NET target:          UNCHANGED (net10.0)
```
