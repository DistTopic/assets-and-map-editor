# Architecture

This document describes the high-level structure and design decisions of **Assets And Map Editor**.

---

## Overview

The solution contains two C# projects:

```
assets-and-map-editor/
└── src/
    ├── App/             # Main Avalonia UI application
    └── OTB/             # File format parsing library
```

The **App** project handles all user interface concerns and orchestrates operations. The **OTB** project is a standalone, UI-agnostic library responsible for reading and writing the various Tibia file formats (DAT, SPR, OTB, OTBM). This separation keeps format logic testable and reusable.

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| UI Framework | [Avalonia UI](https://avaloniaui.net/) 11.3 |
| Application Framework | .NET 10 |
| Language | C# 13 |
| MVVM | CommunityToolkit.Mvvm 8.4 (source generators) |
| Icons | Projektanker FontAwesome via Avalonia |
| Compression | SharpCompress 0.38 |
| Theme | Catppuccin Mocha (dark) |

---

## Project: `AssetsAndMapEditor.OTB`

This library is the core data layer. It has no dependency on Avalonia or any UI framework.

| File | Responsibility |
|------|---------------|
| `DatFile.cs` | Parses Tibia DAT files (multi-protocol: 854, 860, 960+, PStory, Numb) |
| `DatThingType.cs` | Data model for a single DAT thing definition |
| `SprFile.cs` | Parses and writes Tibia SPR files with full RGBA alpha |
| `OtbFile.cs` | Parses and serializes OTB binary tree files |
| `OtbItem.cs` / `OtbData.cs` | OTB item and collection data models |
| `OtbEnums.cs` | Binary tree and item flag enumerations |
| `OtbmFile.cs` / `MapData.cs` | OTBM map file reader and data model |
| `BrushSystem.cs` | XML brush definition loading and resolution |
| `ObdCodec.cs` | Object Builder Data (OBD) import/export codec |
| `ItemsCatalog.cs` | Item ID-to-name lookup used by the map editor |

---

## Project: `AssetsAndMapEditor.App`

This project follows the **MVVM** pattern as implemented by Avalonia and CommunityToolkit.Mvvm.

### Key Directories

| Directory | Contents |
|-----------|---------|
| `ViewModels/` | Observable view models — `MainWindowViewModel` is the root |
| `Controls/` | Custom Avalonia controls (`MapCanvasControl`, `MinimapOverlayControl`) |
| `Converters/` | XAML value converters for data binding |

### Main Components

**`MainWindowViewModel`** is the central coordinator. It:
- Owns one or two `SessionViewModel` instances (split view)
- Routes user commands (save, export, merge, transplant)
- Manages unsaved-changes state and close confirmation

**`SessionViewModel`** represents a single open workspace. It holds references to the loaded `OtbFile`, `DatFile`, `SprFile`, and `MapData`, and exposes the item lists and selected item state to the views.

**`MapCanvasControl`** is a custom `Control` that renders the OTBM map using Avalonia's `DrawingContext`. It owns the current viewport, handles mouse input for tile placement and pan, and fires tile events to the view model.

**`MinimapOverlayControl`** is a semi-transparent floating panel that renders a scaled-down overview of the map and allows viewport navigation.

---

## Data Flow

```
User Interaction
     │
     ▼
ViewModels (MVVM commands/properties)
     │
     ▼
OTB Library (file I/O, data models)
     │
     ▼
Disk (DAT / SPR / OTB / OTBM / XML)
```

All file I/O is handled by the OTB library. View models translate between library models and bindable observable properties. Views are purely declarative.

---

## Session State

Application state is persisted to a JSON file (`AppSettings.json`) in the OS-appropriate user data directory. This includes:

- Last opened file paths for each session slot
- Window layout and panel sizes
- Viewport position for the map canvas
- Toggle states (view menu, ghost floor, overlays)

Session state is loaded on startup and saved on clean exit.

---

## CI/CD Pipeline

Releases are automated via GitHub Actions (`.github/workflows/release.yml`). On every `v*` tag push:

1. Six parallel build jobs produce self-contained single-file executables for all supported platforms.
2. SHA-256 checksums are computed and bundled as `checksums.sha256`.
3. A GitHub Release is created with all artifacts and release notes extracted from `CHANGELOG.md`.

The release is automatically flagged as a pre-release when the tag contains `preview`, `rc`, `alpha`, or `beta`.

---

## Design Decisions

**Why Avalonia?** True native cross-platform desktop with no browser dependency or Electron overhead, full .NET ecosystem access, and a productive XAML-based UI model familiar to WPF/WinUI developers.

**Why a separate OTB library?** Keeping file format logic in a UI-agnostic library makes it possible to test format parsers in isolation and reuse the library in other tools without pulling in Avalonia.

**Why single-file self-contained executables?** Zero install friction for end users — download, verify, run. No .NET runtime installation, no side effects on the user's system.
