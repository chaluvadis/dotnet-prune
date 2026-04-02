# Issue: Search Within Findings

## Title
Search Within Findings

## Description
Full-text search/filter across all findings

## Problem Statement
In large codebases with hundreds of findings, users need a way to quickly locate specific items without scrolling through the entire list.

## Proposed Solution
Add a search bar at the top of the findings tree:
1. Search input filters results as user types
2. Search matches against symbol name, file path, and type
3. Clear button to reset filter
4. Result count shown ("5 of 123 items")

Support regex mode for advanced users.

## User Benefit / Impact
Low - Useful for large codebases to quickly locate specific unused items.

## Priority
Low

## Acceptance Criteria
- [ ] Search bar in tree view header
- [ ] Filters results as user types
- [ ] Matches against name, path, and type
- [ ] Clear button resets filter
- [ ] Result count displayed
- [ ] Optional regex mode

## Technical Notes
- Debounce search input to prevent UI thrashing
- Consider case-insensitive default with case-sensitive option