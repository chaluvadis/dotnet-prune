---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Rich tooltips explaining symbol kinds, confidence scores, and recommendations to help users understand findings without external documentation.

## Problem Statement

Users encounter unfamiliar terms and concepts when reviewing findings. Without inline help, they must consult external documentation, breaking their workflow.

## Proposed Solution

Implement inline help tooltips:
- Hover over symbol kind icon shows explanation
- Tooltip for confidence scores explains what they mean
- Help for filter options and their effects
- Links to more detailed documentation
- Contextual help based on current view/state

## User Benefit / Impact

Medium - Reduces need for external documentation and helps users understand findings in context.

## Priority

Medium

## Acceptance Criteria

- [ ] Symbol kind icons have explanatory tooltips
- [ ] Confidence score has tooltip explaining levels
- [ ] Filter controls have tooltip guidance
- [ ] Links to full documentation available
- [ ] Tooltips are concise and helpful

## Technical Notes

- Use VS Code hover/markdown for rich tooltips
- Tooltips should be skimmable (bullets, not paragraphs)
- Consider accessibility (screen reader support)