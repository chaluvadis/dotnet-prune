# Changelog

All notable changes to the DotNetPrune extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Advanced Filtering System**
  - Filter findings by symbol kind (Method, Property, Field, Parameter, Type, etc.)
  - Filter by confidence level (0-100%)
  - Filter by project name
  - Full-text search across all findings
  - "Clear All Filters" command to reset filters

- **Confidence and Severity Metrics**
  - Each finding now includes a confidence percentage (0-100%)
  - Severity levels (error, warning, information, hint) based on symbol characteristics
  - Public symbols have lower confidence (may be used externally)
  - Private/internal symbols with high confidence marked as warnings

- **VS Code Integration**
  - Problems panel integration - findings appear as diagnostics
  - Inline code highlighting in the editor
  - Rich hover tooltips with detailed information
  - Quick Fix (Code Actions) support for unused code
  - Delete unused code directly from editor
  - Ignore findings from context menu

- **Export Capabilities**
  - Export findings to JSON format
  - Export to CSV for spreadsheet analysis
  - Export to Markdown for documentation
  - Export to HTML with professional styling and color-coded badges
  - Export includes summary statistics and grouped findings

- **User Experience Improvements**
  - Ignore findings to hide from future analyses
  - Delete unused code with confirmation dialog
  - View detailed finding information in webview panel
  - Enhanced tooltips in tree view showing file paths
  - Icons for different symbol kinds

- **Configuration System**
  - Comprehensive settings for analysis behavior
  - UI customization options (highlighting, confidence display, severity display)
  - Performance settings (caching, parallel analysis)
  - Integration settings (Problems panel, Code Actions)
  - Analysis mode: strict (fewer false positives) or loose (comprehensive)
  - Configurable exclusion/inclusion patterns
  - Configurable symbol kind filters

- **Documentation**
  - Comprehensive User Guide with step-by-step instructions
  - Developer Guide with architecture overview and contribution guidelines
  - Troubleshooting section with common issues and solutions
  - Configuration reference

### Changed
- Enhanced Finding model to include confidence and severity fields
- Improved tree view organization with better icon support
- Updated analyzer to calculate confidence and severity for each finding
- Refined filtering to work with the new metrics

### Technical Details
- Added `config.ts` for centralized configuration management
- Added `filter.ts` for finding filtering logic
- Added `diagnostics.ts` for Problems panel integration
- Added `codeActions.ts` for Quick Fix provider
- Added `decorator.ts` for inline code highlighting
- Added `export.ts` for multi-format export functionality
- Added `FindingMetrics.cs` for confidence and severity calculation
- Updated `Models.cs` to include confidence and severity fields
- Enhanced `Configuration.cs` with new analysis options

## [0.0.4] - Previous Release

### Features
- Hierarchical tree view displaying findings by Solution → Project → File
- Smart file filtering for .NET-related files only
- Direct analysis integration running FindUnused analyzer
- Interactive navigation to source files
- Solution-aware organization
- Copy file path context menu
- Copy project name context menu

### Technical
- Bundled FindUnused.dll analyzer tool
- Real-time processing of analyzer output
- File type filtering for .cs, .sln, .slnx, .csproj
- Automatic solution/project discovery

## Future Roadmap

### Planned Features
- **Dead Code Detection**: Identify unreachable code paths (experimental)
- **TODO/HACK Comments**: Surface technical debt markers
- **Bulk Actions**: Bulk ignore or delete multiple findings at once
- **Enhanced Code Context**: Show code snippets in tooltips
- **Unit Tests**: Comprehensive test coverage for extension and analyzer
- **Integration Tests**: End-to-end testing with sample projects
- **Performance Caching**: Persistent caching between VS Code sessions
- **Multi-Target Support**: Enhanced support for Blazor, Xamarin, MAUI projects
- **Custom Rules**: User-defined rules for project-specific patterns

### Under Consideration
- Configurable analysis rules
- Code smell detection
- Metrics dashboard
- Team collaboration features
- CI/CD integration
- Visual Studio support

## Release Notes

### Version 0.0.5 (Upcoming)
This major update transforms DotNetPrune into a comprehensive code quality tool with advanced filtering, diagnostics integration, export capabilities, and actionable Quick Fixes. The addition of confidence and severity metrics helps developers prioritize code cleanup efforts and reduce false positives.

Key highlights:
- 🎯 Filter and search to find exactly what you need
- 📊 Confidence levels help prioritize cleanup
- 🔧 Quick Fixes to delete unused code instantly
- 📋 Export professional reports for code reviews
- ✨ Inline highlights make unused code visible
- ⚡ Problems panel integration for seamless workflow

---

For more information, see the [User Guide](docs/USER_GUIDE.md) and [Developer Guide](docs/DEVELOPER_GUIDE.md).
