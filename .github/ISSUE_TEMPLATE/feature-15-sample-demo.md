---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Run analysis on a built-in small demo solution to showcase capabilities for new users.

## Problem Statement

New users want to understand what the tool does before running it on their own codebase. A demo mode would help them explore features without risk.

## Proposed Solution

Create sample demo:
- Built-in small C# solution (5-10 files)
- Pre-populated with known unused code patterns
- "Run Demo" button in welcome/tutorial
- Step-by-step walkthrough using demo
- Reset demo to original state

## User Benefit / Impact

Medium - Helps new users understand what the tool does in a risk-free environment.

## Priority

Medium

## Acceptance Criteria

- [ ] Demo solution available in extension
- [ ] "Run Demo" option in welcome screen
- [ ] Demo showcases key features
- [ ] Demo can be reset to original state
- [ ] Demo explains each step clearly

## Technical Notes

- Keep demo solution small (< 1MB)
- Include variety of unused code patterns
- Consider bundling or lazy-loading demo