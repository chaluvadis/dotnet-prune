# Issue: Sample Analysis Demo

## Title
Sample Analysis Demo

## Description
Run analysis on a built-in small demo solution to showcase capabilities

## Problem Statement
Users don't understand what the tool does until they run it on their own code. A demo would help them see the features in action without risk.

## Proposed Solution
Include a small demo project bundled with the extension:
1. Demo solution contains known unused code patterns
2. "Run Demo Analysis" command in Command Palette
3. Runs analysis on demo files only
4. Finds pre-placed dead code items
5. Shows expected findings as example output

User can interact with these known findings to understand the UI.

## User Benefit / Impact
Medium - Helps new users understand what the tool does before using on their codebase.

## Priority
Medium

## Acceptance Criteria
- [ ] Demo project included in extension
- [ ] Command to run demo analysis
- [ ] Demo runs on isolated virtual workspace
- [ ] Expected findings documented
- [ ] "View Demo Results" quick access
- [ ] "Explore Demo" option opens demo in editor

## Technical Notes
- Keep demo solution small (< 10 files)
- Embed demo as string resources
- Extract to temp directory for analysis
- Clean up after demo completes