# File Format Reference

This document describes the Tibia game file formats that **Assets And Map Editor** reads and writes.

---

## DAT — Client Data File

The DAT file defines all visual thing definitions used by the Tibia client: items, outfits, effects, and missiles.

**Typical filename:** `Tibia.dat`, `things.dat`

**Format variants supported:**

| Protocol | Signature | Notes |
|----------|-----------|-------|
| 860      | Standard  | Most common OTServer client version |
| 960+     | Extended  | Extended flags and extra fields |
| PStory   | `0x4B1E2CAA` → version 854 | Protocol Story variant |
| Numb     | Signature-detected | Numb protocol variant |

Each thing definition (`DatThingType`) contains data such as dimensions, animation frames, sprite IDs, stack order, and behavior flags.

---

## SPR — Sprite Data File

The SPR file stores the pixel data for all sprites referenced by the DAT file.

**Typical filename:** `Tibia.spr`, `things.spr`

- Each sprite is 32×32 pixels with full RGBA alpha channel.
- Sprites are indexed by a 32-bit ID, referenced from DAT thing definitions.
- The editor preserves the alpha channel on export.

---

## OTB — Object Type Binary

The OTB file is the server-side item definition file used by OTServer-based game servers.

**Typical filename:** `items.otb`

- Uses a binary tree data structure with typed nodes.
- Defines item attributes such as type, flags, weight, and server-side ID.
- The editor can create, edit, and delete entries, and supports importing definitions from OBD files.

---

## OTBM — Open Tibia Binary Map

The OTBM format is the map file format used by OTServer-based servers.

**Typical filename:** `*.otbm`

- Binary tree structure encoding tile data, item placements, towns, waypoints, and world data.
- The editor renders the map on a canvas and supports tile painting using the brush system.

---

## XML Brushes

The brush system defines named collections of tiles and items used for structured painting (walls, borders, grounds, doodads, etc.).

**Location in this project:** `src/App/data/brushes/`

| File | Contents |
|------|---------|
| `grounds.xml` | Ground tile brush definitions |
| `walls.xml` | Wall brush definitions with directional segments |
| `borders.xml` | Border transition rules |
| `doodads.xml` | Decorative object brushes |
| `creatures.xml` | Creature placement brushes |
| `tilesets.xml` | Tileset groupings for the palette |

---

## OBD — Object Builder Data

The OBD format is produced by the [ObjectBuilder](https://github.com/ottools/ObjectBuilder) tool and contains a single item's sprite and definition data in a portable format.

The editor supports importing OBD files to create new items and exporting existing items to OBD for use in other tools.
