---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Save baseline and compare findings between sessions to show progress in reducing dead code over time.

## Problem Statement

Users want to track their progress in cleaning up dead code. Without baseline comparison, there's no way to see improvement or regression between analysis runs.

## Proposed Solution

Implement baseline comparison:
- Save baseline snapshot of findings
- Store baseline in local workspace storage
- On subsequent runs, compare with baseline
- Show added/removed/changed findings
- Summary of progress: items cleaned up vs new dead code

## User Benefit / Impact

Low - Useful for tracking cleanup progress over time, especially in large projects with ongoing maintenance.

## Priority

Low

## Acceptance Criteria

- [ ] Option to save current findings as baseline
- [ ] Display comparison view showing changes
- [ ] Show count of: resolved (cleaned), new, unchanged
- [ ] Option to reset or update baseline
- [ ] Clear visualization of progress over time

## Technical Notes

- Store baseline as JSON in workspace or extension storage
- Use unique identifiers for findings to track across runs
- Consider time-series storage for trend analysis