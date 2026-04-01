# Issue: Incremental Analysis

## Title
Incremental Analysis

## Description
Only re-analyze changed files since last run using file hash comparison

## Problem Statement
Running full analysis on large solutions takes significant time. Users who regularly run the tool want faster feedback on changes without re-analyzing the entire codebase.

## Proposed Solution
Implement a caching mechanism:
1. Store file hashes of all analyzed source files after each run
2. On subsequent runs, compare current hashes against stored hashes
3. Only re-analyze files that have changed
4. Propagate changes to dependent files (if a base class changes, derived classes may need re-analysis)

Provide a "Force Full Analysis" option for cases where cache may be corrupted.

## User Benefit / Impact
Medium - Speeds up repeated analysis on large solutions, improving developer workflow.

## Priority
Medium

## Acceptance Criteria
- [ ] File hash cache persists across sessions
- [ ] Changed files are re-analyzed automatically
- [ ] Dependent files propagate changes correctly
- [ ] Cache can be cleared via command
- [ ] Cache size is reasonable (< 10MB for typical solution)

## Technical Notes
- Use SHA256 for file hashing
- Store cache in .dotnetprune/cache.json
- Handle file deletions gracefully
- Invalidate cache on analyzer version changes