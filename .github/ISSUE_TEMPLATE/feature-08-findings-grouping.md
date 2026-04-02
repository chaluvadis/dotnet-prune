---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Group all unused members of the same class/interface together to improve navigation when cleaning up a single type.

## Problem Statement

Currently, findings are shown as a flat list. Users working on cleaning up a specific class must manually find all related members scattered across the list.

## Proposed Solution

Implement related findings grouping:
- Parent-child hierarchy in findings tree
- Classes/Interfaces as parent nodes
- Unused members nested under their parent type
- Expand/collapse functionality
- Filter by type to focus on specific classes

## User Benefit / Impact

Medium - Improves navigation when cleaning up a single type, reducing the time needed to review all findings for one class.

## Priority

Medium

## Acceptance Criteria

- [ ] Findings displayed in hierarchical tree view
- [ ] Types are parent nodes with member children
- [ ] Expand/collapse functionality works
- [ ] Filtering maintains hierarchy
- [ ] Collapse all / expand all options available

## Technical Notes

- Build type-member relationship during analysis
- Use VS Code TreeView API for hierarchical display
- Consider lazy loading of member details