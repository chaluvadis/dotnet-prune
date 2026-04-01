---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Step-by-step walkthrough explaining how to run analysis and interpret results for first-time users.

## Problem Statement

New users struggle to understand how to use the tool effectively. Without guidance, they may not discover all features or understand how to interpret findings.

## Proposed Solution

Create a first-run tutorial:
- Welcome screen on first installation explaining the tool's purpose
- Step-by-step walkthrough:
  1. How to run analysis (command palette, context menu, button)
  2. Understanding the results view
  3. How to review and act on findings
  4. Configuration options
- Option to replay tutorial from help menu
- Interactive elements to guide users through actual steps

## User Benefit / Impact

High - Improves initial user experience and helps users get value from the tool quickly.

## Priority

High

## Acceptance Criteria

- [ ] Tutorial triggers on first extension activation
- [ ] At least 4 tutorial steps with clear instructions
- [ ] Users can skip tutorial
- [ ] "Replay tutorial" option in help menu
- [ ] Progress indicator shows current step

## Technical Notes

- Use VS Code welcome page API
- Store "has seen tutorial" flag in extension state
- Make tutorial skippable but easy to access later