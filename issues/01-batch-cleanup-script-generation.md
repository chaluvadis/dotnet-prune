# Issue: Batch Cleanup Script Generation

## Title
Batch Cleanup Script Generation

## Description
Select multiple unused items and generate a cleanup script (either individual deletions or a single refactoring script)

## Problem Statement
Currently, users must delete or refactor each unused item individually, which is time-consuming when dealing with dozens of unused items. There is no way to generate a consolidated script that can be reviewed and applied in one batch.

## Proposed Solution
Implement a multi-select feature in the tree view that allows users to select multiple unused items. Provide an option to generate:
1. Individual deletion scripts (one file per item)
2. A consolidated refactoring script that combines all deletions/refactorings

The generated script should be previewable before execution and include proper ordering to handle dependencies correctly.

## User Benefit / Impact
High - Directly addresses the core use case of removing dead code efficiently. Users can review a batch script before applying it, reducing the risk of unintended deletions.

## Priority
High

## Acceptance Criteria
- [ ] Users can Ctrl+Click or Shift+Click to select multiple items in the tree view
- [ ] "Generate Cleanup Script" context menu option appears when multiple items selected
- [ ] Generated script shows proper dependency ordering
- [ ] Script can be previewed in editor before execution
- [ ] Execute option applies all deletions/refactorings
- [ ] Undo capability exists for applied script

## Technical Notes
- Consider using Roslyn's code fix provider for safe refactorings
- Track deletion order based on dependency graph (items used by others deleted last)
- Support both deletion and comment-out modes