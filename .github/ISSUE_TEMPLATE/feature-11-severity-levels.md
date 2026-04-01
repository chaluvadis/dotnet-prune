---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Distinguish between "definitely unused" (high confidence) and "possibly unused" (reflection, dynamic calls) with severity levels.

## Problem Statement

Not all findings are equal - some are clearly unused while others might be used in ways the analyzer cannot detect. Users need severity levels to prioritize cleanup.

## Proposed Solution

Implement severity levels:
- Critical/High: Definitely unused (no references found, private visibility)
- Medium: Possibly unused (public API, might be used externally)
- Low: Likely used (reflection, dynamic calls detected)
- Color-coded icons for each severity
- Filter by severity level
- Tooltip explains reasoning

## User Benefit / Impact

High - Helps prioritize cleanup efforts by distinguishing certain vs uncertain findings. Essential for users to confidently delete code.

## Priority

High

## Acceptance Criteria

- [ ] Each finding has assigned severity level
- [ ] Visual indicators (icons/colors) for each severity
- [ ] Filter by severity available
- [ ] Tooltip shows why severity was assigned
- [ ] Default sort by severity (highest first)