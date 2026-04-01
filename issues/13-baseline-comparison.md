# Issue: Baseline Comparison

## Title
Baseline Comparison

## Description
Save baseline and compare findings between sessions to show progress

## Problem Statement
Users want to track their cleanup progress over time. There's no way to compare current findings against a previous state to see how much dead code has been removed.

## Proposed Solution
Implement baseline tracking:
1. "Save Baseline" command stores current findings
2. "Compare with Baseline" shows diff view
3. Items removed since baseline shown as "Resolved"
4. Items added since baseline shown as "New"
5. Summary shows cleanup percentage

Provide ability to have multiple named baselines.

## User Benefit / Impact
Low - Useful for tracking cleanup over time and demonstrating progress.

## Priority
Low

## Acceptance Criteria
- [ ] Save baseline command
- [ ] Baseline stored in local workspace
- [ ] Diff view shows added/removed items
- [ ] Summary statistics (items removed, percentage)
- [ ] Multiple named baselines support
- [ ] Delete/rename baseline options

## Technical Notes
- Store baseline as JSON with timestamps
- Track symbol identity, not just names (handle renames)
- Use semantic diff for accurate comparison