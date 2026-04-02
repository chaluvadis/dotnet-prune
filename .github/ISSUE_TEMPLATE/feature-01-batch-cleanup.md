---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Select multiple unused items and generate a cleanup script (either individual deletions or a single refactoring script).

## Problem Statement

Users want to clean up multiple unused items at once, but currently must handle each item individually. Generating bulk cleanup scripts would significantly improve workflow efficiency.

## Proposed Solution

Implement batch cleanup functionality:
- Multi-select capability in the findings tree view
- Options: generate individual delete commands or a single consolidation script
- Support for generating PowerShell/bash scripts or C# refactoring code
- Preview mode showing what will be deleted before execution

## User Benefit / Impact

High - Directly addresses the core use case of removing dead code efficiently. Major productivity improvement for bulk cleanup.

## Priority

High

## Acceptance Criteria

- [ ] Users can select multiple items via checkbox or Ctrl+click
- [ ] Bulk delete option generates script with all selected items
- [ ] Bulk refactor option generates consolidated cleanup code
- [ ] Preview shows all items that will be affected
- [ ] Confirmation required before execution

## Technical Notes

- Generate cross-platform scripts (PowerShell for Windows, bash for Unix)
- Handle file dependencies (if deleting class, handle related files)
- Support undo/revert capability