# Changelog

All notable changes to **Assets And Map Editor** will be documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [v2.0.0-preview] — 2026-04-05

> All improvements and fixes delivered after the first public preview release.

### Added

#### Asset Editor
- **Icon grid view** for the client items panel — toggle between the traditional list and a compact icon grid using the button next to the search bar ([#2], [`1433042`])
- **Preferences window** accessible from File → Preferences, allowing customization of items per page (range 10–1000, persisted across sessions) ([#2], [`1433042`])
- **Resizable panels** — all three main columns (client items, editor, OTB detail) can now be resized via draggable splitters ([#5], [`040ab99`])
- **Animate Always editable** — the "Animate Always" property in the animation card is now a toggleable checkbox instead of a read-only text field ([#6], [`afc8bb9`])
- **Animation Play state preserved** — the Play checkbox remains active when switching between items that support animation ([#4], [`c537e58`])
- **Animated sprite previews** in merge and batch transplant confirmation dialogs ([`d0d86a5`])
- **Category-aware transplant** — transplant and merge menus now support per-category operations (Items, Outfits, Effects, Missiles) ([`8136d2b`])
- **HasCharges and FloorChange** properties added to the DAT item model ([`71a9978`])

#### Map Editor
- **Map Properties dialog** — view and edit map metadata (description, dimensions, house file, spawn file) ([`0385f3c`])
- **Map Statistics dialog** — see tile counts, item counts, and spawn/house summaries ([`0385f3c`], [`1d7e986`])
- **Map menu** with properties, statistics, and cleanup operations ([`1d7e986`])
- **Town navigation** with a searchable town list in the properties panel ([`7dbc39d`])
- **Minimap viewport-centered rendering** for large maps ([`6d01a10`])
- **Collection items tab** — organize items into named collections from the palette ([`10934d6`])

#### Startup & Session
- **Welcome window** with session history — on launch, choose from recent sessions or start fresh ([`4291a58`])
- **Town list restored on startup** — towns now populate correctly when restoring a previous session ([`24827d9`])

#### Infrastructure
- **CI/CD build workflow** with automated release creation and GitFlow branching strategy ([`f2e8e0f`])
- **Protocol 1100 support** with short DAT signatures ([`7ca90ee`])

### Fixed

- Collection tab header and view now update immediately when adding items via the context menu ([`3735fab`])
- Catalog always displays the full OTB item list regardless of active tab ([`9373102`])
- Town list appears by default when opening a map for the first time ([`8afab56`])
- Town list populates correctly after restoring a session on startup ([`24827d9`])
- Merge dialog auto-detects Extended, Improved Animations, Frame Groups, and Transparency from parse results ([`8743016`])
- Merge duplicate detection now hashes all frame groups, frames, and patterns for accuracy ([`9e4ffa2`])
- Merge uses the best frame group for outfit animation previews ([`a4ef273`])
- Transplant skips items with empty sprites instead of crashing ([`0d6b442`])
- Page navigation and filter changes no longer crash when the selected item index becomes stale ([`1433042`])

### Changed

- Selection highlight color changed from green to magenta for better visibility ([`7ca90ee`])
- Brush panel removed from the default layout to reduce clutter ([`7ca90ee`])
- SPR transparency handling decoupled from extended header for cleaner format support ([`43f7c06`])
- DAT flags aligned with Object Builder reference for compatibility ([`171d508`])
- Left panel widened from 220px to 270px for better icon grid fit ([`1433042`])

---

## [v1.0.0-preview] — 2026-03-28

> First public preview release of **Assets And Map Editor** — a cross-platform visual editor
> for Tibia assets (DAT, SPR, OTB) and maps (OTBM), built with
> [Avalonia UI](https://avaloniaui.net/) and .NET 10.

### Asset Editor

- DAT, SPR, and OTB file loading with async progress indicators ([`8a117f6`], [`0906810`])
- Unified editor layout with OTB panel and inline sprite strip ([`0906810`])
- Full width × height sprite composition grid with animation preview ([`d5c755f`])
- Copy and paste sprites between items using raw pixel data ([`b01e12a`], [`4772a17`], [`4a94076`])
- Drag-and-drop sprites from sprite list to composition grid ([`a2930f5`])
- Export sprites to clipboard with full RGBA alpha channel ([`b0ec99f`])
- Multi-select and bulk delete for OTB and client item lists ([`f9ce8a5`], [`cf15f23`])
- Navigate-to-item and context menu OTB entry creation ([`cf15f23`])
- OBD (Object Builder Data) import and export ([`1177109`])
- Simultaneous DAT + SPR save alongside OTB ([`f648873`])
- Unified Save All (Ctrl+S) for OTB, DAT, SPR, and Map ([`b8db725`])
- Session persistence — viewport, file paths, and state across restarts ([`63a13d1`], [`1177109`])
- Close confirmation dialog (Save / Discard / Cancel) on unsaved changes
- Mismatch filter — identify client items with OTB animation mismatches ([`ce719b5`])
- Find Unmapped Client Items — scan DAT for entries without OTB mappings
- Find Duplicate Items — pixel-based SHA-256 duplicate detection across all client items
- Compact Sprites — detect empty or unreferenced sprite slots and remap to fill gaps
- Protocol detection for PStory and Numb DAT files (signature `0x4B1E2CAA` → version 854)
- Targeted sprite refresh on modification instead of full reload ([`71f6d22`], [`3a766f6`])
- Selection highlighting across all UI lists ([`d3d35ce`])
- Sprite export dialog with image-only format options ([`7c672a0`])

### Cross-Session Operations

- Cross-session DAT/SPR merge with sprite-hash-based deduplication ([`0184027`], [`ae8c076`])
- Multi-item batch transplant across sessions with image-based duplicate detection ([`399e55b`], [`41e1764`])
- Category-aware merge and transplant supporting Items, Outfits, Effects, and Missiles ([`ce719b5`])
- Per-category breakdowns in merge and transplant confirmation dialogs
- Transplant operations correctly mark sessions as unsaved ([`b738c39`])

### Map Editor

- Integrated OTBM map editor with full canvas editing capabilities ([`19828db`], [`0c9695d`])
- Palette system with collections, catalog, and keyboard navigation ([`af99480`])
- Full XML brush system — loader, writer, catalog, visual editor, and palette integration ([`252011e`], [`80d1e10`])
- Brush editor window with border edge editing and visual previews ([`cfa9028`])
- Cross-file brush type resolution (wall, carpet, table, doodads) ([`8e61769`], [`bc4ddb6`])
- Tileset hierarchy grouping with brush categorization ([`4367025`])
- Wall auto-alignment with automatic border placement ([`ccb643b`])
- Border Automagic — toggle between raw tile and brush-based placement ([`39b24eb`])
- Minimap overlay — navigable, movable, and resizable ([`c85f8ed`], [`e94ef6b`])
- Ghost floor rendering for higher and lower floors with adjustable opacity ([`aeef0a4`], [`d664f19`], [`7388940`])
- On-canvas hover tooltips displaying item IDs and names ([`5e4a5d4`])
- Client box and zone visualization toggles ([`a8e39a1`])
- Map catalog pagination with first/last page navigation ([`2d79297`])

### User Interface

- Split view with tab drag-reorder and palette brushes ([`5c561cc`])
- View menu toggles persisted across restarts ([`806544a`], [`ef42e01`])
- Catppuccin Mocha dark theme
- All UI labels in English

### Build & Distribution

- GitHub Actions CI/CD pipeline with automated release creation ([`ce719b5`])
- Cross-platform builds: Windows x64/ARM64, macOS x64/ARM64, Linux x64/ARM64
- Single-file self-contained executables — no runtime installation required

---

**Full Changelog**: [`8a117f6...v1.0.0-preview`]

<!-- Commit reference links -->
[`8a117f6`]: https://github.com/DistTopic/assets-and-map-editor/commit/8a117f6
[`0906810`]: https://github.com/DistTopic/assets-and-map-editor/commit/0906810
[`19828db`]: https://github.com/DistTopic/assets-and-map-editor/commit/19828db
[`0c9695d`]: https://github.com/DistTopic/assets-and-map-editor/commit/0c9695d
[`af99480`]: https://github.com/DistTopic/assets-and-map-editor/commit/af99480
[`80d1e10`]: https://github.com/DistTopic/assets-and-map-editor/commit/80d1e10
[`252011e`]: https://github.com/DistTopic/assets-and-map-editor/commit/252011e
[`bc4ddb6`]: https://github.com/DistTopic/assets-and-map-editor/commit/bc4ddb6
[`4367025`]: https://github.com/DistTopic/assets-and-map-editor/commit/4367025
[`ccb643b`]: https://github.com/DistTopic/assets-and-map-editor/commit/ccb643b
[`cfa9028`]: https://github.com/DistTopic/assets-and-map-editor/commit/cfa9028
[`8e61769`]: https://github.com/DistTopic/assets-and-map-editor/commit/8e61769
[`39b24eb`]: https://github.com/DistTopic/assets-and-map-editor/commit/39b24eb
[`c85f8ed`]: https://github.com/DistTopic/assets-and-map-editor/commit/c85f8ed
[`e94ef6b`]: https://github.com/DistTopic/assets-and-map-editor/commit/e94ef6b
[`d5c755f`]: https://github.com/DistTopic/assets-and-map-editor/commit/d5c755f
[`f9ce8a5`]: https://github.com/DistTopic/assets-and-map-editor/commit/f9ce8a5
[`cf15f23`]: https://github.com/DistTopic/assets-and-map-editor/commit/cf15f23
[`f648873`]: https://github.com/DistTopic/assets-and-map-editor/commit/f648873
[`2d79297`]: https://github.com/DistTopic/assets-and-map-editor/commit/2d79297
[`b738c39`]: https://github.com/DistTopic/assets-and-map-editor/commit/b738c39
[`41e1764`]: https://github.com/DistTopic/assets-and-map-editor/commit/41e1764
[`399e55b`]: https://github.com/DistTopic/assets-and-map-editor/commit/399e55b
[`1177109`]: https://github.com/DistTopic/assets-and-map-editor/commit/1177109
[`63a13d1`]: https://github.com/DistTopic/assets-and-map-editor/commit/63a13d1
[`b01e12a`]: https://github.com/DistTopic/assets-and-map-editor/commit/b01e12a
[`4772a17`]: https://github.com/DistTopic/assets-and-map-editor/commit/4772a17
[`4a94076`]: https://github.com/DistTopic/assets-and-map-editor/commit/4a94076
[`a2930f5`]: https://github.com/DistTopic/assets-and-map-editor/commit/a2930f5
[`b0ec99f`]: https://github.com/DistTopic/assets-and-map-editor/commit/b0ec99f
[`71f6d22`]: https://github.com/DistTopic/assets-and-map-editor/commit/71f6d22
[`3a766f6`]: https://github.com/DistTopic/assets-and-map-editor/commit/3a766f6
[`7c672a0`]: https://github.com/DistTopic/assets-and-map-editor/commit/7c672a0
[`b8db725`]: https://github.com/DistTopic/assets-and-map-editor/commit/b8db725
[`806544a`]: https://github.com/DistTopic/assets-and-map-editor/commit/806544a
[`ef42e01`]: https://github.com/DistTopic/assets-and-map-editor/commit/ef42e01
[`5e4a5d4`]: https://github.com/DistTopic/assets-and-map-editor/commit/5e4a5d4
[`a8e39a1`]: https://github.com/DistTopic/assets-and-map-editor/commit/a8e39a1
[`aeef0a4`]: https://github.com/DistTopic/assets-and-map-editor/commit/aeef0a4
[`d664f19`]: https://github.com/DistTopic/assets-and-map-editor/commit/d664f19
[`7388940`]: https://github.com/DistTopic/assets-and-map-editor/commit/7388940
[`0184027`]: https://github.com/DistTopic/assets-and-map-editor/commit/0184027
[`ae8c076`]: https://github.com/DistTopic/assets-and-map-editor/commit/ae8c076
[`ce719b5`]: https://github.com/DistTopic/assets-and-map-editor/commit/ce719b5
[`d3d35ce`]: https://github.com/DistTopic/assets-and-map-editor/commit/d3d35ce
[`5c561cc`]: https://github.com/DistTopic/assets-and-map-editor/commit/5c561cc
[`8a117f6...v1.0.0-preview`]: https://github.com/DistTopic/assets-and-map-editor/compare/8a117f6...v1.0.0-preview

<!-- Unreleased commit reference links -->
[#2]: https://github.com/DistTopic/assets-and-map-editor/issues/2
[#4]: https://github.com/DistTopic/assets-and-map-editor/issues/4
[#5]: https://github.com/DistTopic/assets-and-map-editor/issues/5
[#6]: https://github.com/DistTopic/assets-and-map-editor/issues/6
[`1433042`]: https://github.com/DistTopic/assets-and-map-editor/commit/1433042
[`c537e58`]: https://github.com/DistTopic/assets-and-map-editor/commit/c537e58
[`040ab99`]: https://github.com/DistTopic/assets-and-map-editor/commit/040ab99
[`4291a58`]: https://github.com/DistTopic/assets-and-map-editor/commit/4291a58
[`24827d9`]: https://github.com/DistTopic/assets-and-map-editor/commit/24827d9
[`afc8bb9`]: https://github.com/DistTopic/assets-and-map-editor/commit/afc8bb9
[`3735fab`]: https://github.com/DistTopic/assets-and-map-editor/commit/3735fab
[`9373102`]: https://github.com/DistTopic/assets-and-map-editor/commit/9373102
[`10934d6`]: https://github.com/DistTopic/assets-and-map-editor/commit/10934d6
[`8afab56`]: https://github.com/DistTopic/assets-and-map-editor/commit/8afab56
[`7dbc39d`]: https://github.com/DistTopic/assets-and-map-editor/commit/7dbc39d
[`6d01a10`]: https://github.com/DistTopic/assets-and-map-editor/commit/6d01a10
[`0d6b442`]: https://github.com/DistTopic/assets-and-map-editor/commit/0d6b442
[`9e4ffa2`]: https://github.com/DistTopic/assets-and-map-editor/commit/9e4ffa2
[`a4ef273`]: https://github.com/DistTopic/assets-and-map-editor/commit/a4ef273
[`d0d86a5`]: https://github.com/DistTopic/assets-and-map-editor/commit/d0d86a5
[`43f7c06`]: https://github.com/DistTopic/assets-and-map-editor/commit/43f7c06
[`8743016`]: https://github.com/DistTopic/assets-and-map-editor/commit/8743016
[`8136d2b`]: https://github.com/DistTopic/assets-and-map-editor/commit/8136d2b
[`171d508`]: https://github.com/DistTopic/assets-and-map-editor/commit/171d508
[`71a9978`]: https://github.com/DistTopic/assets-and-map-editor/commit/71a9978
[`7ca90ee`]: https://github.com/DistTopic/assets-and-map-editor/commit/7ca90ee
[`1d7e986`]: https://github.com/DistTopic/assets-and-map-editor/commit/1d7e986
[`0385f3c`]: https://github.com/DistTopic/assets-and-map-editor/commit/0385f3c
[`f2e8e0f`]: https://github.com/DistTopic/assets-and-map-editor/commit/f2e8e0f
