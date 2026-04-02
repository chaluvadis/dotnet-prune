# Issue: Exclusion Patterns Configuration

## Title
Exclusion Patterns Configuration

## Description
Allow users to configure glob patterns to exclude from analysis (e.g., `**/Generated/**/*.cs`, `**/*Designer.cs`)

## Problem Statement
Generated code (Designer.cs, auto-generated files) and test files often appear as unused but should not be included in cleanup recommendations. Users need a way to exclude these patterns without modifying their codebase.

## Proposed Solution
Add a configuration file (`.dotnetprune.json`) or VS Code settings that accepts glob patterns. Patterns are matched against file paths and excluded from analysis results. Provide sensible defaults for common patterns.

## User Benefit / Impact
High - Reduces unwanted findings in generated/test code, making the tool more practical for real-world use.

## Priority
High

## Acceptance Criteria
- [ ] Support for `.dotnetprune.json` config file in solution root
- [ ] Support for VS Code workspace settings override
- [ ] Default patterns for common generated files (*Designer.cs, *Generated.cs, obj/, bin/)
- [ ] UI shows which patterns are active
- [ ] Patterns apply to both file path and symbol kind

## Technical Notes
- Use fast-glob or minimatch for pattern matching
- Support negation patterns (!**/test/**) to include specific files
- Cache compiled patterns for performance