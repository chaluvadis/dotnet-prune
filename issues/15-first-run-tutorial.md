# Issue: First-Run Tutorial

## Title
First-Run Tutorial

## Description
Step-by-step walkthrough explaining how to run analysis and interpret results

## Problem Statement
New users don't know how to:
1. Run the analyzer
2. Interpret findings
3. Use the results to clean up code

Without guidance, they may abandon the tool or make mistakes.

## Proposed Solution
Implement an interactive tutorial:
1. Detect first run (no settings file exists)
2. Show welcome modal with "Start Tutorial" option
3. Walk through steps with highlighted UI elements:
   - Run analysis command location
   - Findings tree explanation
   - How to delete/ignore items
   - Confidence level meaning
4. "Don't show again" option
5. Access tutorial via Help menu

## User Benefit / Impact
High - Improves initial user experience, reducing abandonment.

## Priority
High

## Acceptance Criteria
- [ ] Welcome dialog on first run
- [ ] Step-by-step walkthrough UI
- [ ] Highlights each UI element as it's explained
- [ ] Progress indicator (Step X of Y)
- [ ] Skip and "Don't show again" options
- [ ] Tutorial accessible from Help menu

## Technical Notes
- Use VS Code's WebView for tutorial UI
- Track "first run" state in extension storage
- Consider using existing walkthrough extension APIs