# Feature Ideation for DotNetPrune

Based on the current implementation gaps and focus areas (developer productivity, debugging experience, code quality, onboarding improvements), here are validated feature ideas:

## Developer Productivity

| # | Feature | Description | Impact |
|---|---------|-------------|--------|
| 1 | **Batch Cleanup Script Generation** | Select multiple unused items and generate a cleanup script (either individual deletions or a single refactoring script) | High - Directly addresses the core use case of removing dead code |
| 2 | **Confidence-Based Filtering** | Filter findings by confidence level (high/medium/low) to reduce noise and focus on high-confidence deletions | High - Improves usability by filtering false positives |
| 3 | **Exclusion Patterns Configuration** | Allow users to configure glob patterns to exclude from analysis (e.g., `**/Generated/**/*.cs`, `**/*Designer.cs`) | High - Reduces unwanted findings in generated/test code |
| 4 | **Incremental Analysis** | Only re-analyze changed files since last run using file hash comparison | Medium - Speeds up repeated analysis on large solutions |
| 5 | **Background Auto-Scan** | Optionally run analysis automatically on file save with debouncing | Medium - Provides real-time feedback |

## Debugging Experience

| # | Feature | Description | Impact |
|---|---------|-------------|--------|
| 6 | **Code Preview Panel** | Show a preview of the unused code directly in the tree view tooltip or side panel | High - Helps developers understand what they're looking at before navigating |
| 7 | **Reference Analysis** | Show reference count and locations to understand why code might appear unused | Medium - Helps distinguish false positives from true dead code |
| 8 | **Related Findings Grouping** | Group all unused members of the same class/interface together | Medium - Improves navigation when cleaning up a single type |
| 9 | **Search Within Findings** | Full-text search/filter across all findings | Low - Useful for large codebases |

## Code Quality

| # | Feature | Description | Impact |
|---|---------|-------------|--------|
| 10 | **Dead Code Metrics Dashboard** | Show statistics: total unused items, by symbol kind, by project, trend over time | Medium - Provides visibility into codebase health |
| 11 | **Severity Levels** | Distinguish between "definitely unused" (high confidence) vs "possibly unused" (reflection, dynamic calls) | High - Helps prioritize cleanup efforts |
| 12 | **Baseline Comparison** | Save baseline and compare findings between sessions to show progress | Low - Useful for tracking cleanup over time |
| 13 | **Export Reports** | Export findings to JSON/CSV for integration with other tools | Low - Enables further analysis outside VS Code |

## Onboarding Improvements

| # | Feature | Description | Impact |
|---|---------|-------------|--------|
| 14 | **First-Run Tutorial** | Step-by-step walkthrough explaining how to run analysis and interpret results | High - Improves initial user experience |
| 15 | **Sample Analysis Demo** | Run analysis on a built-in small demo solution to showcase capabilities | Medium - Helps new users understand what the tool does |
| 16 | **Inline Help Tooltips** | Rich tooltips explaining symbol kinds, confidence scores, and recommendations | Medium - Reduces need for external documentation |
| 17 | **Better handling of .NET patterns** | Improve detection for: extension methods, partial classes, attribute-based code, dynamic/reflection usage | High - Reduces false positives which frustrates users |

## Implementation Priority Recommendation

**Phase 1 (High Impact, Lower Effort):**
- #2 Confidence-Based Filtering
- #3 Exclusion Patterns Configuration
- #17 Better handling of .NET patterns

**Phase 2 (High Impact, Higher Effort):**
- #1 Batch Cleanup Script Generation
- #6 Code Preview Panel
- #14 First-Run Tutorial

**Phase 3 (Medium Impact):**
- #10 Dead Code Metrics Dashboard
- #4 Incremental Analysis
- #7 Reference Analysis

**Phase 4 (Lower Priority):**
- #8, #9, #11, #12, #13, #15, #16

---

*This list addresses the identified gaps from the README: "Update the analyzer to handle different dotnet project types" and "Optimize the analyzer for large solutions" while also expanding into user experience improvements.*