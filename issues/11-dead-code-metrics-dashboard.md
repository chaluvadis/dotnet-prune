# Issue: Dead Code Metrics Dashboard

## Title
Dead Code Metrics Dashboard

## Description
Show statistics: total unused items, by symbol kind, by project, trend over time

## Problem Statement
Users want visibility into the overall health of their codebase. Metrics help track progress over time and identify areas needing attention.

## Proposed Solution
Create a dedicated "Metrics" view/tab that displays:
1. **Overview**: Total unused items count
2. **By Symbol Kind**: Breakdown (methods, classes, fields, properties)
3. **By Project**: Which projects have most dead code
4. **Trend Over Time**: Historical comparison (if baseline exists)
5. **Quick Actions**: "Focus on Project X" filter

Visualize data with charts/bars.

## User Benefit / Impact
Medium - Provides visibility into codebase health and helps prioritize cleanup efforts.

## Priority
Medium

## Acceptance Criteria
- [ ] Metrics tab in extension sidebar
- [ ] Total count and breakdown displayed
- [ ] Filter by project functionality
- [ ] Export metrics option
- [ ] Responsive design for different screen sizes

## Technical Notes
- Store historical data in local cache
- Use lightweight charting library (Chart.js or similar)
- Consider webview for rich visualizations