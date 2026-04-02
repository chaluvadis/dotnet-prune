# Issue: Background Auto-Scan

## Title
Background Auto-Scan

## Description
Optionally run analysis automatically on file save with debouncing

## Problem Statement
Users must manually trigger analysis after making changes. Real-time feedback on newly introduced dead code would help maintain a clean codebase continuously.

## Proposed Solution
Add a setting to enable background scanning:
1. Detect file save events in the workspace
2. Debounce rapid saves (e.g., wait 2 seconds after last save)
3. Run incremental analysis on changed files
4. Update findings tree with new results
5. Show notification when new dead code is detected

Provide UI controls to enable/disable and set debounce interval.

## User Benefit / Impact
Medium - Provides real-time feedback, helping users catch dead code early.

## Priority
Medium

## Acceptance Criteria
- [ ] Toggle in settings to enable/disable auto-scan
- [ ] Debounce prevents analysis thrashing on rapid saves
- [ ] Non-intrusive notification when new findings detected
- [ ] Performance impact minimal when editing (no UI blocking)
- [ ] Can be paused during intensive editing sessions

## Technical Notes
- Use VS Code's file system watcher with debounce
- Background analysis should not block UI
- Consider memory usage for keeping analysis state