# Issue: Confidence-Based Filtering

## Title
Confidence-Based Filtering

## Description
Filter findings by confidence level (high/medium/low) to reduce noise and focus on high-confidence deletions

## Problem Statement
The analyzer returns all findings regardless of how likely they are to be true positives. Users want to focus on high-confidence items first and optionally filter out items that might be used dynamically or through reflection.

## Proposed Solution
Add confidence level metadata to each finding. Implement UI filters in the tree view to:
- Show only high confidence (definitely unused)
- Show high + medium confidence
- Show all (including low confidence)

Confidence levels should be based on static analysis signals: direct symbol references vs potential dynamic usage.

## User Benefit / Impact
High - Improves usability by filtering false positives, allowing users to focus on safe deletions first.

## Priority
High

## Acceptance Criteria
- [ ] Each finding displays confidence indicator (High/Medium/Low)
- [ ] Filter dropdown in tree view allows selecting confidence level
- [ ] Filtering persists across sessions
- [ ] Tooltip explains what each confidence level means

## Technical Notes
- High: No dynamic calls, no reflection, direct symbol references checked
- Medium: Potential dynamic usage detected
- Low: Heavy use of reflection, dynamic code patterns