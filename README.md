<p align="center">
  <h1 align="center">Assets And Map Editor</h1>
  <p align="center">
    A cross-platform visual editor for Tibia assets and maps.
    <br />
    <a href="docs/GETTING_STARTED.md"><strong>Getting Started »</strong></a>
    &nbsp;&middot;&nbsp;
    <a href="CHANGELOG.md"><strong>Changelog</strong></a>
    &nbsp;&middot;&nbsp;
    <a href="https://github.com/DistTopic/assets-and-map-editor/issues/new?template=bug_report.yml">Report Bug</a>
    &nbsp;&middot;&nbsp;
    <a href="https://github.com/DistTopic/assets-and-map-editor/issues/new?template=feature_request.yml">Request Feature</a>
  </p>
</p>

<p align="center">
  <a href="https://github.com/DistTopic/assets-and-map-editor/releases/latest"><img src="https://img.shields.io/github/v/release/DistTopic/assets-and-map-editor?include_prereleases&style=flat-square&label=release" alt="Latest Release" /></a>
  <a href="https://github.com/DistTopic/assets-and-map-editor/actions/workflows/release.yml"><img src="https://img.shields.io/github/actions/workflow/status/DistTopic/assets-and-map-editor/release.yml?style=flat-square&label=build" alt="Build Status" /></a>
  <a href="https://github.com/DistTopic/assets-and-map-editor/actions/workflows/security-scan.yml"><img src="https://img.shields.io/github/actions/workflow/status/DistTopic/assets-and-map-editor/security-scan.yml?style=flat-square&label=security+scan" alt="Security Scan" /></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Avalonia-11.3-7B2BFC?style=flat-square" alt="Avalonia 11.3" />
  <img src="https://img.shields.io/badge/C%23-13-239120?style=flat-square&logo=csharp" alt="C# 13" />
  <a href="LICENSE"><img src="https://img.shields.io/github/license/DistTopic/assets-and-map-editor?style=flat-square" alt="License" /></a>
  <img src="https://img.shields.io/badge/platforms-Windows%20%7C%20macOS%20%7C%20Linux-informational?style=flat-square" alt="Platforms" />
  <a href="https://github.com/DistTopic/assets-and-map-editor/releases/latest"><img src="https://img.shields.io/github/downloads/DistTopic/assets-and-map-editor/total?style=flat-square&label=downloads" alt="Total Downloads" /></a>
</p>

---

## Overview

**Assets And Map Editor** is a desktop application for editing Tibia game assets and maps. It provides a unified workspace for managing DAT, SPR, OTB, and OTBM files with a modern, dark-themed interface.

Built with [Avalonia UI](https://avaloniaui.net/) for true cross-platform support — runs natively on Windows, macOS (Intel & Apple Silicon), and Linux without browser dependencies or Electron overhead.

## Features

### Asset Editing
- Load, inspect, and edit **DAT**, **SPR**, and **OTB** files
- Full sprite composition grid with animation preview
- Copy, paste, and drag-and-drop sprites between items
- Export sprites with full RGBA alpha channel
- OBD (Object Builder Data) import and export
- Bulk operations: multi-select, delete, find duplicates, compact sprites
- Mismatch detection between client and server item definitions

### Map Editing
- Integrated **OTBM** map editor with canvas, palette, and catalog
- XML-based brush system with visual editor
- Wall auto-alignment and border automagic
- Minimap overlay — navigable, movable, and resizable
- Ghost floor rendering for multi-level editing

### Cross-Session Operations
- Merge assets across sessions with sprite-hash deduplication
- Batch transplant items, outfits, effects, and missiles between versions
- Category-aware operations with per-type breakdowns

### Workflow
- Unified **Save All** (Ctrl+S) for OTB, DAT, SPR, and Map
- Session persistence — viewport, paths, and state across restarts
- Unsaved changes protection with confirmation dialogs

## Downloads

Pre-built binaries are available for every major platform. No runtime installation required — each release is a self-contained single-file executable.

| Platform | Architecture | Download |
|----------|-------------|----------|
| Windows  | x64         | [`disttopic-assets-and-map-editor-windows-x64.zip`](https://github.com/DistTopic/assets-and-map-editor/releases/latest) |
| Windows  | ARM64       | [`disttopic-assets-and-map-editor-windows-arm64.zip`](https://github.com/DistTopic/assets-and-map-editor/releases/latest) |
| macOS    | Apple Silicon (M1+) | [`disttopic-assets-and-map-editor-macos-arm64.zip`](https://github.com/DistTopic/assets-and-map-editor/releases/latest) |
| macOS    | Intel x64   | [`disttopic-assets-and-map-editor-macos-x64.zip`](https://github.com/DistTopic/assets-and-map-editor/releases/latest) |
| Linux    | x64         | [`disttopic-assets-and-map-editor-linux-x64.zip`](https://github.com/DistTopic/assets-and-map-editor/releases/latest) |
| Linux    | ARM64       | [`disttopic-assets-and-map-editor-linux-arm64.zip`](https://github.com/DistTopic/assets-and-map-editor/releases/latest) |

> See all versions on the [Releases](https://github.com/DistTopic/assets-and-map-editor/releases) page.

Each release also includes a `checksums.sha256` file. Verify your download before running:

```bash
# Linux / macOS
sha256sum --check checksums.sha256

# Windows (PowerShell)
Get-FileHash disttopic-assets-and-map-editor-windows-x64.zip -Algorithm SHA256
```

> **Windows users:** Windows SmartScreen may show an "Unknown publisher" warning because the binary is not yet commercially code-signed. You can verify authenticity by checking the SHA-256 hash and reviewing the [CI build log](https://github.com/DistTopic/assets-and-map-editor/actions). See [Build and Release Verification](docs/BUILD_AND_VERIFICATION.md) for full details.

## Building from Source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
git clone https://github.com/DistTopic/assets-and-map-editor.git
cd assets-and-map-editor
dotnet build src/App/AssetsAndMapEditor.App.csproj
```

### Publish (self-contained)

```bash
dotnet publish src/App/AssetsAndMapEditor.App.csproj \
  -c Release \
  -r <RID> \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish/
```

Replace `<RID>` with your target runtime: `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, or `linux-arm64`.

## Documentation

| Document | Description |
|----------|-------------|
| [Getting Started](docs/GETTING_STARTED.md) | First-time setup, download verification, and basic usage |
| [Architecture](docs/ARCHITECTURE.md) | Project structure and design decisions |
| [File Formats](docs/FILE_FORMATS.md) | DAT, SPR, OTB, OTBM, and brush format reference |
| [Build & Verification](docs/BUILD_AND_VERIFICATION.md) | Reproducing builds and verifying release integrity |
| [Changelog](CHANGELOG.md) | Version history with commit references |
| [Contributing](CONTRIBUTING.md) | How to contribute to the project |
| [Code of Conduct](CODE_OF_CONDUCT.md) | Community standards |
| [Security Policy](SECURITY.md) | Reporting vulnerabilities and verifying releases |

## Project Structure

```
assets-and-map-editor/
├── src/
│   ├── App/                  # Main application (Avalonia UI)
│   │   ├── Controls/         # Custom UI controls (MapCanvas, SpriteStrip, etc.)
│   │   ├── Converters/       # XAML value converters
│   │   ├── Models/           # Data models (DAT, SPR, OTB, OTBM, Brushes)
│   │   ├── ViewModels/       # MVVM view models
│   │   └── Views/            # AXAML views and windows
│   └── OTB/                  # OTB file format library
├── .github/workflows/        # CI/CD pipeline
├── docs/                     # Extended documentation
├── CHANGELOG.md
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── SECURITY.md
└── LICENSE
```

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

---

<p align="center">
  Made with Avalonia UI and .NET &nbsp;·&nbsp; Maintained by <a href="https://github.com/DistTopic">DistTopic</a>
</p>
