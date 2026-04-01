# Issue: Severity Levels

## Title
Severity Levels

## Description
Distinguish between "definitely unused" (high confidence) vs "possibly unused" (reflection, dynamic calls)

## Problem Statement
Users cannot easily distinguish which findings are safe to delete vs risky. All items appear equal, leading to hesitation or accidentally removing code that might be used.

## Proposed Solution
Implement a severity/certainty system:
1. **High**: Definitely unused - no dynamic calls, no reflection
2. **Medium**: Possibly unused - potential dynamic usage detected
3. **Low**: Likely used - heavy reflection/dynamic patterns

Visual indicators: icons, colors, badges. Filter by severity in tree view.

## User Benefit / Impact
High - Helps prioritize cleanup efforts, focusing on safe deletions first.

## Priority
High

## Acceptance Criteria
- [ ] Each finding has severity level indicator
- [ ] Color coding (green/yellow/red) for severity
- [ ] Filter to show only certain severity levels
- [ ] Tooltip explains severity determination
- [ ] Severity shown in preview panel

## Technical Notes
- Combine with confidence-based filtering feature
- Severity tied to detection algorithm confidence
- Consider project-wide severity summary