# Issue: Code Preview Panel

## Title
Code Preview Panel

## Description
Show a preview of the unused code directly in the tree view tooltip or side panel

## Problem Statement
Users must open each file to see the unused code before deciding to delete it. This creates friction and makes it harder to understand the context of what's being flagged.

## Proposed Solution
Implement a preview panel (side-by-side or tooltip-based) that shows:
1. The source code of the unused item
2. Line numbers for reference
3. Syntax highlighting
4. Quick actions (delete, ignore, open in editor)

The panel should appear when hovering over an item or clicking to expand.

## User Benefit / Impact
High - Helps developers understand what they're looking at before navigating, reducing context switching.

## Priority
High

## Acceptance Criteria
- [ ] Hovering over a finding shows code preview
- [ ] Side panel can be toggled open for selected item
- [ ] Syntax highlighting applied correctly
- [ ] Line numbers displayed
- [ ] "Open in Editor" action available
- [ ] Preview updates when selection changes

## Technical Notes
- Use VS Code's Webview for syntax highlighting
- Lazy-load preview content for performance
- Consider virtualized rendering for long files