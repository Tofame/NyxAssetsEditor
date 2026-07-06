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
- [SixLabors.ImageSharp](https://sixlabors.com/products/imagesharp/) — image processing
