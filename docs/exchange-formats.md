# Exchange Formats Specification

Nyx Assets Editor supports importing and exporting things and sprites across multiple binary and text file formats. This document specifies the exchange formats supported by **NyxAssets** and **Nyx Assets Editor**.

---

## 1. Single-Thing Exchange (`.nyx-thing` & `.obd`)

Users can import or export single thing definitions (items, outfits, effects, missiles) through clipboard actions or context menus.

### `.nyx-thing` (JSON Exchange Format)

`.nyx-thing` is a human-readable JSON exchange format designed for interoperability and version control.

- **Structure**: Encapsulates `ThingKind`, metadata properties, flag dictionaries, pattern dimensions, frame groups, and per-frame duration arrays.
- **Embedded Sprites**: Can optionally embed sprite RGBA pixel data as Base64-encoded strings or references.
- **Codecs**: Handled via `ThingDocumentJsonCodec` in `Services/Exchange`.

### `.obd` (Object Builder Document)

`.obd` is the legacy binary format created by OTServ Object Builder for single item/outfit exchange.

- **Binary Payload**: Contains LZMA/Zlib compressed sprite buffers alongside binary attribute tags.
- **Codecs**: Parsed and serialized via `ObdThingCodec` in `Services/Exchange`.

---

## 2. Catalog & Archive Packages

### Legacy Formats (`.dat` & `.spr`)

- **`.dat` (Thing Catalog)**: Binary file containing catalog signatures, thing counts, property flags (V1-V6 protocols), sprite IDs, and pattern mapping matrices.
- **`.spr` (Sprite Archive)**: Binary file containing 32×32 pixel RGBA sprites compressed using Nyx RLE (Run-Length Encoding).
- **`.otfi` (OTClient Format Information)**: Optional TOML/INI metadata sidecar specifying metadata flags (`extended`, `transparency`, `frame-durations`, `frame-groups`).

### Modern Formats (`.things` & `.assets`)

- **`.things`**: Modern binary catalog format used by NyxFramework, eliminating legacy flag byte limits and offering native 64-bit ID spaces.
- **`.assets`**: High-performance packaged sprite archive format supporting direct memory-mapped lookups, RGBA channels, and custom compression codecs.
