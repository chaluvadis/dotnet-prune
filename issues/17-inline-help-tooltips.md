# Issue: Inline Help Tooltips

## Title
Inline Help Tooltips

## Description
Rich tooltips explaining symbol kinds, confidence scores, and recommendations

## Problem Statement
Users don't understand what technical terms mean (symbol kinds, confidence scores). They need explanations without leaving the IDE to check documentation.

## Proposed Solution
Add contextual tooltips:
1. Hover over symbol kind → explains what "Method" vs "Property" means
2. Hover over confidence score → explains how it's calculated
3. Hover over actions → explains what they do
4. Use information icons (?) for additional help
5. Links to documentation for deep dives

Implement as VS Code's native hover provider or custom tooltips.

## User Benefit / Impact
Medium - Reduces need for external documentation and improves self-service understanding.

## Priority
Medium

## Acceptance Criteria
- [ ] Tooltips on symbol kind badges
- [ ] Tooltips on confidence level indicators
- [ ] Tooltips on action buttons
- [ ] Information icon for each section
- [ ] Consistent tooltip styling
- [ ] Links to full documentation

## Technical Notes
- Use VS Code's MarkdownString for tooltips
- Include examples in tooltip content
- Support keyboard-accessible tooltips
- Consider tooltip anchoring for long content