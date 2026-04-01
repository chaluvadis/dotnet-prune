---
name: Feature Issue
title: ""
labels: "feature"
assignees: ""
---

## Description

Improve detection for extension methods, partial classes, attribute-based code, and dynamic/reflection usage to reduce false positives.

## Problem Statement

The current analyzer produces false positives for:
- Extension methods that are called statically
- Partial classes with split implementations
- Code using attributes (serialization, dependency injection)
- Code accessed via reflection or dynamic calls
- XAML-referenced code in UI frameworks

Users lose trust in the tool when these common patterns are flagged as unused.

## Proposed Solution

Enhance the analyzer to recognize and handle these .NET patterns:
1. **Extension Methods**: Detect when methods are used as extension methods (syntax `args.Method()`) even if no direct reference exists
2. **Partial Classes**: Understand partial class declarations and combine analysis across all parts
3. **Attribute-Based Code**: Recognize common attributes (JsonProperty, SerializeField, [Inject], etc.) that keep code alive
4. **Reflection/Dynamic**: Detect reflection-based access patterns and mark as lower confidence rather than unused
5. **XAML References**: Parse XAML/baml files for resource references

## User Benefit / Impact

High - Reduces false positives which frustrate users and erode trust in the tool. Essential for adoption in real-world .NET projects.

## Priority

High

## Acceptance Criteria

- [ ] Extension methods are correctly identified as used
- [ ] Partial class members across files are analyzed together
- [ ] Common attribute patterns are recognized and excluded from findings
- [ ] Reflection-based usage is flagged with lower confidence rather than as unused
- [ ] Confidence level reflects analyzer certainty for each finding

## Technical Notes

- May require parsing additional file types (.xaml, .razor)
- Consider Roslyn analyzers for deeper static analysis
- Build a database of common .NET patterns that appear unused but aren't