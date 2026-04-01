---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Optionally run analysis automatically on file save with debouncing to provide real-time feedback.

## Problem Statement

Users want immediate feedback when code becomes unused. Currently, they must manually trigger analysis each time, creating friction in the workflow.

## Proposed Solution

Implement background auto-scan:
- Option in settings to enable auto-scan
- Debounce file saves (wait for multiple rapid saves to settle)
- Configurable debounce delay (default 2 seconds)
- Visual indicator when background scan is running
- Non-blocking analysis (doesn't interrupt editing)
- Option to auto-cleanup or just notify

## User Benefit / Impact

Medium - Provides real-time feedback on dead code, improving the development workflow.

## Priority

Medium

## Acceptance Criteria

- [ ] Settings toggle to enable/disable auto-scan
- [ ] Debouncing prevents analysis spam on rapid saves
- [ ] Visual indicator shows when background scan active
- [ ] Analysis doesn't block editing or cause lag
- [ ] Works with single file and bulk saves

## Technical Notes

- Use file system watcher for change detection
- Debounce with configurable delay
- Priority: low background task
- Consider battery/performance impact on laptops