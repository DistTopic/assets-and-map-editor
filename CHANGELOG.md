# Changelog

All notable changes to Assets And Map Editor will be documented in this file.

## [v1.0.0] - 2026-03-28

### Added
- **Category-aware mass migration** — Merge Session and Batch Transplant now support Items, Outfits, Effects, and Missiles (not just Items)
- **Mismatch filter** — Filter client item catalog by OTB animation mismatch via dropdown
- **Find Unmapped Client Items** — Scan DAT for items without OTB entries, preview and batch-create staged OTB entries
- **Find Duplicate Items** — Pixel-based SHA-256 duplicate detection across all client items
- **Compact Sprites** — Find empty/unreferenced sprite slots, remap to fill gaps
- **Loading indicators** — Progress bar and async loading for DAT/SPR/OTB operations
- **Close confirmation** — Save/Discard/Cancel dialog on window close with unsaved changes
- **Cross-session merge** — Merge all items from a source session with sprite-based deduplication
- **Multi-item transplant** — Batch transplant selected items across sessions with duplicate detection
- **Minimap overlay** — Navigable, movable, resizable minimap on the map editor
- **Brush system** — Full XML brush loader/writer, catalog, visual editor, palette integration
- **Wall auto-alignment** — Automatic wall border placement
- **Border Automagic** — Toggle between raw and brush-based tile placement
- **Map editor** — Integrated OTBM map editor with palette, catalog, and canvas
- **Sprite operations** — Copy/paste, drag-drop, export with RGBA alpha
- **Composition grid** — Full width×height sprite composition with animation
- **OBD import/export** — Object Builder format support
- **Session persistence** — Viewport per session, remember map across restarts
- **Multi-select & delete** — For both OTB and client item lists
- **CI/CD** — GitHub Actions pipeline for Windows x64 and macOS ARM64 releases

### Fixed
- Protocol detection for PStory/Numb DAT files (0x4B1E2CAA → 854)
- Transplanted items now render correctly in target session
- Ghost floor rendering z-order and opacity
- Minimap crash during render pass
- SPR save alongside DAT
- Various crash fixes and UI improvements

### Changed
- All UI labels in English (Save All, Modified)
- Merge/transplant dialogs show per-category breakdowns
