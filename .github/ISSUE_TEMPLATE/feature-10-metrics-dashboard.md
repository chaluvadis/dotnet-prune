---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Show statistics: total unused items, by symbol kind, by project, and trend over time.

## Problem Statement

Users want visibility into the overall health of their codebase and track progress in reducing dead code over time. Currently, there's no way to see aggregate metrics or trends.

## Proposed Solution

Create a metrics dashboard:
- Summary cards: total unused items, files affected, estimated cleanup effort
- Breakdown by symbol kind (method, field, class, property)
- Breakdown by project/module
- Historical trend chart (if baseline data exists)
- Export metrics for reporting

## User Benefit / Impact

Medium - Provides visibility into codebase health and helps track cleanup progress over time.

## Priority

Medium

## Acceptance Criteria

- [ ] Dashboard view accessible from sidebar
- [ ] Total unused items count displayed
- [ ] Breakdown by symbol kind shown
- [ ] Breakdown by project/module shown
- [ ] Trend data available when baselines saved

## Technical Notes

- Consider using chart library for trends visualization
- Store historical data in local storage or workspace
- Performance: aggregate data during analysis, not on every view