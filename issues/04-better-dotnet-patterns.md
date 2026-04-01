# Issue: Better Handling of .NET Patterns

## Title
Better Handling of .NET Patterns

## Description
Improve detection for: extension methods, partial classes, attribute-based code, dynamic/reflection usage

## Problem Statement
The current analyzer produces false positives for commonly used .NET patterns:
- Extension methods that appear unused but are called via static syntax
- Partial classes split across files
- Code that uses attributes for registration/activation
- Code accessed through reflection or dynamic calls

These false positives frustrate users and reduce trust in the tool.

## Proposed Solution
Enhance the analyzer to recognize these patterns:
1. **Extension Methods**: Detect usage via `Method(arg)` syntax and import statements
2. **Partial Classes**: Aggregate findings across all partial files before reporting
3. **Attribute-Based Code**: Recognize usage via `Attribute` patterns and `[assembly: ...]` declarations
4. **Reflection/Dynamic**: Mark items accessed via `Type.GetMethod`, `Activator.CreateInstance`, `dynamic` as lower confidence rather than unreferenced

Add pattern-specific heuristics and flag findings with detected patterns.

## User Benefit / Impact
High - Reduces false positives which frustrates users and makes the tool more trustworthy for production use.

## Priority
High

## Acceptance Criteria
- [ ] Extension methods with static import usage correctly identified as used
- [ ] Partial class members consolidated - not reported multiple times
- [ ] Attribute-decorated types/members recognized as used
- [ ] Reflection/dynamic usage marked with "possibly used" confidence
- [ ] Performance impact < 10% compared to current baseline

## Technical Notes
- Use Roslyn's symbol exploration to detect extension method imports
- Track partial class parts via Compilation.GetParts()
- Analyze attribute application via AttributeData
- Implement reflection analysis heuristics from known patterns