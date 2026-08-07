# User Guide - Nyx Assets Editor

This guide covers all key user features, tools, and workflows in **Nyx Assets Editor**.

---

## 1. Getting Started & Archive Workflows

Nyx Assets Editor works with two archive formats:
- **Legacy Formats**: `.dat` (Thing definitions catalog) paired with `.spr` (Sprite image archive).
- **Modern Formats**: `.things` (Nyx catalog format) paired with `.assets` (Nyx asset archive).

### Loading Archives

1. Navigate to the **Assets** tab in the main navigation bar.
2. In a **Sprite Archive Viewer** panel, click **Browse** or drag-and-drop a `.spr` / `.assets` file.
3. In a **Things Archive Viewer** panel, click **Browse** or drag-and-drop a matching `.dat` / `.things` file.
4. **Automatic Archive Pairing**: When a Things archive is loaded, it automatically links with the last active Sprite Archive Viewer panel, unlocking live texture previews and rendering.

### OTFI Metadata Settings

When loading classic `.spr` / `.dat` pairs, the editor checks for an `.otfi` metadata file located in the same directory:
- Options such as `extended`, `transparency`, `frame-durations`, and `frame-groups` are parsed automatically.
- Enable **Prefer settings from .otfi** in the loading settings panel to ensure exact parsing.
- If `.otfi` is missing or incomplete, the editor falls back to standard client version detection recommendations.

---

## 2. Browsing & Section Tabs

The Things Archive Viewer splits catalog content into four primary kinds:

| Tab | Kind | Description |
| :--- | :--- | :--- |
| **Items** | `ThingKind.Item` | Map items, equipment, furniture, containers, and ground tiles. |
| **Outfits** | `ThingKind.Outfit` | Player character and monster outfits (creature appearances). |
| **Effects** | `ThingKind.Effect` | Spell animations, impact visuals, and environment particle effects. |
| **Missiles** | `ThingKind.Missile` | Distance projectile animations (arrows, runes, magic bolts). |

### Grid vs List View & Paging

- Switch between **Grid View** (preview tiles) and **List View** (detailed ID and flags list).
- Select page sizes from **50 to 300 items per page** for rapid navigation without UI lag.

---

## 3. Thing Editor

Double-clicking any item in a Things Viewer opens the floating **Thing Editor**.

### Texture Tab

- **Appearance Preview**: Real-time rendering of the thing using `ThingPreviewRenderer`.
- **Direction & Action Controls**: For outfits and missiles, preview North, East, South, and West directions or walking frame sequences.
- **Layer & Frame Sliders**: Cycle through pattern X/Y/Z dimensions, layers, and animation frames.
- **Grid & Crop Overlays**: Toggle tile grid lines and bounds crop boxes to verify sprite alignments.
- **Improved Animation**: Adjust per-frame durations and loop modes for modern client animations.

### Properties Tab

- **Common Flags**: Toggle boolean behaviors such as `Stackable`, `Rotatable`, `Container`, `Force Use`, `Multi Use`, `Unpassable`, `Block Missile`, and `Pickupable`.
- **Protocol Specific Flags**: Flag names and availability adapt automatically based on the detected client protocol (V1 to V6). See [DAT Protocols & Flags](dat_protocols.md).

---

## 4. Thing Finder

Each Things Viewer panel includes an integrated **Thing Finder** tool (accessible via **Find Thing** or `Ctrl+F`):

- **Enable Filter Checkboxes**: Like multi-edit mode, every field has an explicit enable checkbox. Unchecked fields do not filter results.
- **Multi-Criteria Searching**: Filter items by flags (`Container`, `Stackable`, `Usable`, etc.), pattern dimensions (Width, Height, Layers, Frames), or JSON custom properties.
- **Context Actions**: Right-click results to **Copy ID**, open in Editor, or send directly to the **Looktype Generator**.

---

## 5. Looktype Generator

The **Looktype Generator** (opened from the Assets toolbar) allows creators to assemble outfit appearances and corpse items without modifying source archives:

- **Outfit Composition**: Pick Base Outfit ID, Addons (1 & 2), Mount ID, and Action state (Idle vs Walking).
- **Color Palette Masking**: Mathematical rendering of all 133 Character outfit colors applied across yellow (head), red (body), green (legs), and blue (feet) color masks.
- **Live Lua / XML Code Synchronization**:
  - Automatically generates ready-to-use OTForms Lua or XML code snippet (e.g. `creature.outfit = {type = 136, head = 78, ...}`).
  - Editing Lua or XML in the text box updates the visual preview live in real time.
- **Corpse Integration**: View corpse item previews associated with character/monster outfits.

---

## 6. Replacer

Open **Replacer** from the Assets toolbar to copy an inclusive Thing or Sprite ID range between two loaded archive pairs. Thing replacement copies definitions into the same Thing IDs and maps source sprite pixels onto each existing target Thing's corresponding sprite slots. Raw Sprite replacement remains same-ID.

- Choose different source and target pairs, the Things/Sprites mode, and the From/To IDs.
- Thing mode also selects Items, Outfits, Effects, or Missiles.
- Unavailable IDs are skipped automatically and reported without changing their targets.
- Enable **Create missing target IDs** to append missing Things without gaps. Additional source sprites are deduplicated across the whole batch, appended contiguously at the end of the target Sprite archive, and remapped into the copied Thing definitions. Missing or invalid IDs that cannot be created are still skipped and reported.
- Cross-version replacement keeps each panel's own client-format settings. Empty sprite slots (`0`) remain empty, improved animation timing is converted for legacy targets, and modern outfit frame groups are collapsed when the target format cannot store them.
- When frame amounts differ, the Replacer reports a warning per Thing. Higher-frame sources retain their frames and may append sprites; lower-frame sources reduce the copied definition while surplus target sprites remain untouched and may become unreferenced.
- Use the Replacer title-bar Undo/Redo buttons, or `Ctrl+Z` / `Ctrl+Y`, to reverse or reapply a complete replacement across its affected target viewers.
- A viewer's Replace button opens a drag-and-drop dialog for one selection or preconfigures Replacer from the minimum and maximum IDs of a multi-selection.

---

## 7. Web Export & OxiPNG Optimization

Export archive contents for web applications via **Assets → Web Export**:

- Export sprite sheets and individual PNG images.
- **OxiPNG Integration**: If [oxipng](https://github.com/oxipng/oxipng) is installed on your system `PATH`, Web Export automatically optimizes output PNG sizes:

| Option | Command Flag | Description |
| :--- | :--- | :--- |
| **Default Optimization** | `-o 3 --strip safe` | Standard mid-level PNG optimization effort. |
| **OxiPNG Max** | `-o max` | Maximum compression algorithms (slower export time). |
| **Zopfli Compression** | `-o 3 --zopfli` | Uses oxipng's built-in Zopfli compression engine for smallest file size. |
