---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Full-text search and filter across all findings to quickly locate specific unused code.

## Problem Statement

In large codebases with many findings, users need to search for specific symbols or patterns. Currently, there's no search capability within findings.

## Proposed Solution

Implement search within findings:
- Search box in findings view
- Search by symbol name, file path, or content
- Real-time filtering as user types
- Highlight matches in results
- Recent searches history

## User Benefit / Impact

Low - Useful for large codebases to quickly find specific unused items without scrolling through all findings.

## Priority

Low

## Acceptance Criteria

- [ ] Search input field available in findings panel
- [ ] Search filters results in real-time
- [ ] Supports partial matching
- [ ] Clear button to reset search
- [ ] Search scope includes symbol name and file path