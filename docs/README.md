> [!NOTE]
> **Disclaimer:** Currently this project has **no license**. Please note that "no license" does not mean it is open-source; it means the code is private property and fully mine. As per GitHub's Terms of Service, I, Tofame, retain 100% of my undisputed rights to this repository. In the future there might be added a license which allows for great freedom of usage, however for now, I do not wish to ponder over which one would be good.

# Nyx Assets Editor

Desktop editor for Nyx sprite archives (`.spr` / `.assets`) and things catalogs (`.dat` / `.things`), built with **Avalonia 12** and **NyxAssets 0.2+**.

## Quick start

```bash
dotnet run --project NyxAssetsEditor.csproj
```

1. Open **Assets** from the top navigation bar.
2. Load a sprite archive in a **Sprite Archive Viewer** panel.
3. Load a matching things archive in a **Things Archive Viewer** panel (`.dat` pairs with `.spr`, `.things` pairs with `.assets`).
4. Use section tabs (Items / Outfits / Effects / Missiles) to browse things.
5. Open **Looktype Generator** from the Assets toolbar to create, preview, save, import, or export composed appearances.

For `.spr` / `.dat` pairs, each loader can read `extended`, `transparency`, `frame-durations`, and `frame-groups` from a same-named `.otfi` file. Select **Prefer settings from .otfi** in the loading settings; missing or incomplete files automatically fall back to the recommended settings detected from the archive version.

## Documentation

| Document | Description |
|----------|-------------|
| **[structure.md](structure.md)** | **MVVM layers, View/ViewModel pairing, folder map, code-behind rules** |
| [architecture.md](architecture.md) | Workspace pairing, things sections, persistence |
| [avalonia-performance.md](avalonia-performance.md) | UI performance practices applied in this app |

## Dependencies

- [Avalonia](https://avaloniaui.net/) 12 — cross-platform UI
- [NyxAssets](https://www.nuget.org/packages/NyxAssets) — archive I/O, thing exchange, previews
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) — view models
- [SkiaSharp](https://github.com/mono/SkiaSharp) — image processing
