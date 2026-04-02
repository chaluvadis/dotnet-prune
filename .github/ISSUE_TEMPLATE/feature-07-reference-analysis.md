---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Show reference count and locations to help understand why code might appear unused and distinguish false positives from true dead code.

## Problem Statement

Users need more context about each finding to make informed decisions about deletion. Knowing how many references exist and where they are helps validate findings.

## Proposed Solution

Implement reference analysis:
- For each finding, show reference count
- List all locations where the symbol is referenced
- Distinguish between direct references and indirect (through other symbols)
- Show references even in non-analyzed files (external projects)
- Highlight references in code preview

## User Benefit / Impact

Medium - Helps users distinguish false positives from true dead code, increasing confidence in cleanup decisions.

## Priority

Medium

## Acceptance Criteria

- [ ] Reference count displayed for each finding
- [ ] Click to see all reference locations
- [ ] References link to their locations in code
- [ ] Handle edge cases: external references, generated code
- [ ] Clear messaging when no references found

## Technical Notes

- Use Roslyn for accurate reference counting
- Cache reference data for performance
- Handle partial trust scenarios where not all code is accessible