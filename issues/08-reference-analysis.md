# Issue: Reference Analysis

## Title
Reference Analysis

## Description
Show reference count and locations to understand why code might appear unused

## Problem Statement
Some code appears unused but may have subtle references. Users need visibility into all potential references to distinguish true dead code from false positives.

## Proposed Solution
Add a "References" section to each finding that shows:
1. Total count of direct references found
2. List of reference locations (file:line)
3. Type of reference (direct call, base class usage, delegate usage)
4. Potential references via reflection/dynamic (marked as "possible")

This helps users understand the analyzer's reasoning and make informed decisions.

## User Benefit / Impact
Medium - Helps distinguish false positives from true dead code, building trust in the tool.

## Priority
Medium

## Acceptance Criteria
- [ ] Each finding shows reference count
- [ ] Expandable references section lists locations
- [ ] References clickable to navigate to source
- [ ] "Possible" references marked distinctly
- [ ] Toggle to show/hide zero-reference items

## Technical Notes
- Leverage Roslyn's FindReferences API
- Track reference types for categorization
- Consider performance for large codebases