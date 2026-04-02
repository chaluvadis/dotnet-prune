---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Show a preview of the unused code directly in the tree view tooltip or side panel to help developers understand what they're looking at before navigating.

## Problem Statement

Users must click on each finding and navigate to the code to see what's being flagged. This creates friction, especially when reviewing multiple potential dead code items.

## Proposed Solution

Implement inline code preview:
- Show code snippet in tooltip on hover over findings
- Side panel with full preview for selected item
- Display 5-10 lines of context around the unused code
- Syntax highlighting for the code
- Click to navigate to the full file

## User Benefit / Impact

High - Helps developers quickly understand what each finding represents without leaving their current context.

## Priority

High

## Acceptance Criteria

- [ ] Hover tooltip shows code preview
- [ ] Side panel shows detailed preview when item selected
- [ ] Code is syntax-highlighted
- [ ] Context lines (before/after) are included
- [ ] Click navigates to actual file location

## Technical Notes

- Use VS Code markdown for rich tooltips
- Cache preview data to avoid re-reading files
- Limit preview to prevent memory issues with large files