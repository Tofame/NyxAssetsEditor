> [!NOTE]
> **Disclaimer:** Currently this project has **no license**. Please note that "no license" does not mean it is open-source; it means the code is private property and fully mine. As per GitHub's Terms of Service, I, Tofame, retain 100% of my undisputed rights to this repository. In the future there might be added a license which allows for great freedom of usage, however for now, I do not wish to ponder over which one would be good.

# Nyx Assets Editor Documentation Index

Nyx Assets Editor is a desktop application for viewing, editing, converting, and exporting sprite archives (`.spr` / `.assets`) and thing catalogs (`.dat` / `.things`), built with **Avalonia 12** and **NyxAssets 0.2+**.

---

## Documentation Map

| Guide / Reference | Audience | Description |
| :--- | :--- | :--- |
| **[User Guide](user-guide.md)** | Users / Creators | Complete user manual covering archive viewers, Thing Editor, Thing Finder, Looktype Generator, and Web Export. |
| **[Architecture Overview](architecture.md)** | Developers | High-level system architecture, service graph, canvas docking engine, and persistence models. |
| **[Project Structure & MVVM](structure.md)** | Developers | Detailed breakdown of folder layouts, View ↔ ViewModel resolution, Locator mechanics, and code-behind rules. |
| **[Avalonia Performance](avalonia-performance.md)** | Developers | UI rendering optimization strategies, compiled bindings, lazy previews, and list virtualization. |
| **[DAT Protocols & Flags](dat_protocols.md)** | Devs & Modders | Client protocol versions map (V1–V6), flag configuration schema, and custom `flags_override.toml` setup. |
| **[Exchange Formats](exchange-formats.md)** | Devs & Modders | Specification for `.nyx-thing`, `.obd`, legacy `.spr`/`.dat`, and modern `.assets`/`.things` packages. |
| **[Developer Guide](developer-guide.md)** | Contributors | Build environment setup, asset resolution, project conventions, and adding new screens/features. |

---

## Quick Start

### Build & Run

```bash
# Build & Run the desktop editor (.NET 10 SDK required)
dotnet run --project NyxAssetsEditor.csproj
```

### Basic Workflow

1. Open **Assets** from the top navigation bar.
2. Load a sprite archive in a **Sprite Archive Viewer** panel (`.spr` or `.assets`).
3. Load a matching thing catalog in a **Things Archive Viewer** panel (`.dat` or `.things`).
4. Select tabs (**Items**, **Outfits**, **Effects**, **Missiles**) to filter catalog kinds.
5. Double-click any item or sprite to launch its floating editor.
6. Open **Looktype Generator** from the toolbar to compose appearances and export Lua/XML outfits.
7. Open **Assets → Web Export** to extract and optimize sprites with [oxipng](https://github.com/oxipng/oxipng).

---

## Requirements & Dependencies

- **Runtime & SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/)
- **UI Framework**: [Avalonia 12](https://avaloniaui.net/)
- **Core Engine**: [NyxAssets](https://www.nuget.org/packages/NyxAssets)
- **MVVM Framework**: [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- **Graphics Pipeline**: [SkiaSharp](https://github.com/mono/SkiaSharp)
- **Optional Optimizer**: [OxiPNG](https://github.com/oxipng/oxipng) (required for optimized Web Export)
