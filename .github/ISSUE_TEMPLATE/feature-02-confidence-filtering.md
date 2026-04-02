---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Allow users to filter findings by confidence level (high/medium/low) to reduce noise and focus on high-confidence deletions.

## Problem Statement

The analyzer currently returns all findings without any way to prioritize or filter by confidence. Users are overwhelmed by findings that may include false positives, making it difficult to focus on high-confidence unused code deletions.

## Proposed Solution

Add confidence filtering capabilities to the analysis results:
- Introduce confidence levels for each finding: High (definitely unused), Medium (likely unused), Low (possibly unused)
- Add filter UI controls in the VS Code extension to toggle confidence levels
- Store confidence metadata alongside each finding in the analysis output

## User Benefit / Impact

High - Improves usability by allowing users to filter out false positives and focus on high-confidence deletions. This directly addresses the core use case of removing dead code safely.

## Priority

High

## Acceptance Criteria

- [ ] Each unused item found by the analyzer includes a confidence level (High/Medium/Low)
- [ ] Filter controls allow users to show/hide findings by confidence level
- [ ] Default view shows all confidence levels
- [ ] Filter state persists between sessions
- [ ] Confidence levels are clearly displayed in the findings list

## Technical Notes

- Confidence scoring can be based on: symbol type, reference analysis, .NET pattern recognition
- High confidence: private methods/fields with no references
- Medium confidence: public members that may be used dynamically
- Low confidence: members using reflection, dynamic calls, or extensibility points