# Changelog

All notable changes to **Assets And Map Editor** will be documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
This project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
