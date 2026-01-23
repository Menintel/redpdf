# Changelog

All notable changes to RedPDF will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
### Planned
- Search functionality
- Annotations (highlight, underline, sticky notes)
- Tab support for multiple documents
- Table of Contents navigation
- Recent files

---

## [0.1.0] - 2026-01-23
### Added
- Version tracking in `.csproj` and `CHANGELOG.md`
- Virtual scrolling optimization
- Improved error handling with detailed dialogs

### Fixed
- PDF rendering thread access issues
- PageShadow resource loading order

## [0.0.0] - 2026-01-23
### Added
- Initial project setup with .NET 9 WPF
- VS Code-inspired dark theme
- PDF rendering using Docnet.Core (PDFium)
- Page thumbnail sidebar
- Basic zoom controls (zoom in/out, fit)
- Page navigation (first, previous, next, last)
- Loading overlay with progress indicator
- Page caching for performance
- Custom window chrome with modern styling

### Technical
- MVVM architecture with CommunityToolkit.Mvvm
- Dependency injection ready
- x64 platform target for native PDFium
