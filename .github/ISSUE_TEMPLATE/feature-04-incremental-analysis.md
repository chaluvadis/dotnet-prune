---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Only re-analyze changed files since last run using file hash comparison to improve performance on large solutions.

## Problem Statement

Analyzing large solutions takes significant time. Re-running full analysis after every small change wastes developer time and resources.

## Proposed Solution

Implement incremental analysis:
- Store file hashes from previous analysis run
- On subsequent runs, compare file hashes
- Only analyze files that have changed
- Optionally allow full re-analysis on demand
- Progress indicator shows scanning state (checking vs analyzing)

## User Benefit / Impact

Medium - Speeds up repeated analysis on large solutions, improving developer workflow.

## Priority

Medium

## Acceptance Criteria

- [ ] First run performs full analysis
- [ ] Subsequent runs skip unchanged files
- [ ] Option to force full re-analysis
- [ ] Clear indicator when incremental mode active
- [ ] Hash data persists between sessions

## Technical Notes

- Use SHA256 or similar for file hashing
- Store hash cache in workspace or extension storage
- Handle file deletions (missing hashes indicate deleted files)
- Invalidate cache when project file (.csproj) changes