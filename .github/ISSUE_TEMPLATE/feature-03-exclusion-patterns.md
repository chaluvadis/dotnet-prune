---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Allow users to configure glob patterns to exclude from analysis (e.g., `**/Generated/**/*.cs`, `**/*Designer.cs`).

## Problem Statement

Users want to exclude generated files, designer files, and test files from analysis. Currently, there's no way to configure exclusions, causing unwanted findings in files that shouldn't be analyzed.

## Proposed Solution

Add exclusion pattern configuration:
- Allow users to specify glob patterns in settings
- Support both file-level and folder-level exclusions
- Provide default exclusions for common patterns (*.Designer.cs, *.g.cs, obj/, bin/)
- Validate patterns and show clear error messages for invalid globs

## User Benefit / Impact

High - Reduces unwanted findings in generated/test code, improving signal-to-noise ratio.

## Priority

High

## Acceptance Criteria

- [ ] Settings option to add/remove exclusion patterns
- [ ] Support for common glob patterns (*, **, ?, [])
- [ ] Default exclusion patterns for common generated files
- [ ] Patterns are validated before saving
- [ ] Excluded files are not analyzed and don't appear in results

## Technical Notes

- Use glob library for pattern matching
- Store patterns in VS Code workspace settings
- Consider per-workspace configuration support