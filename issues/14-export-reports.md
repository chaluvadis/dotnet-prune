# Issue: Export Reports

## Title
Export Reports

## Description
Export findings to JSON/CSV for integration with other tools

## Problem Statement
Users want to integrate findings with external tools (CI pipelines, static analysis dashboards, spreadsheet reports). No export functionality exists currently.

## Proposed Solution
Add export functionality:
1. "Export" button in tree view header
2. Format options: JSON, CSV
3. Export options: all findings, filtered findings
4. Include metadata: file path, line, column, symbol kind, confidence
5. Save dialog for file location

## User Benefit / Impact
Low - Enables further analysis outside VS Code and integration with external systems.

## Priority
Low

## Acceptance Criteria
- [ ] Export button in UI
- [ ] JSON format with full metadata
- [ ] CSV format with columns: File, Line, Column, Symbol, Kind, Confidence
- [ ] Export respects current filters
- [ ] Default filename includes date

## Technical Notes
- Use VS Code's save dialog API
- JSON schema should be documented
- Consider markdown export as additional option