# Issue: Related Findings Grouping

## Title
Related Findings Grouping

## Description
Group all unused members of the same class/interface together

## Problem Statement
When cleaning up a class with multiple unused members, users must navigate through individual items one at a time. Grouping related findings would improve navigation efficiency.

## Proposed Solution
Implement hierarchical grouping:
1. Parent node = class/interface name
2. Child nodes = individual unused members
3. Expand/collapse all members of a type

Add a "Group by Type" toggle in the tree view header.

## User Benefit / Impact
Medium - Improves navigation when cleaning up a single type, reducing clicks needed.

## Priority
Medium

## Acceptance Criteria
- [ ] Toggle to group by containing type
- [ ] Parent nodes show count of unused members
- [ ] Expand/collapse all members of a type
- [ ] Grouping persists across sessions
- [ ] Flat list available as alternative view

## Technical Notes
- Use tree view's hierarchical data structure
- Consider grouping by namespace as alternative