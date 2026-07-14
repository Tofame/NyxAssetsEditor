# Architecture

Domain and runtime behavior for the assets workspace. For **MVVM layout, View/ViewModel pairing, and folder conventions**, see **[structure.md](structure.md)**.

Nyx Assets Editor follows **Avalonia MVVM** with feature-oriented folders (`Views/Pages`, `ViewModels/ArchiveLoaders`, etc.). Subfolders organize screens — they do not add extra layers beyond View / ViewModel / Services.

## Folder layout (summary)

See [structure.md](structure.md) for the full MVVM-aware map. Short version:

```
NyxAssetsEditor/
├── Core/                    # App entry, ViewLocator, application root
├── Views/
│   ├── Shell/               # MainWindow
│   ├── Pages/               # Home, Settings, Assets workspace
│   └── ArchiveLoaders/      # Sprite & things floating/docked panels
├── ViewModels/
│   ├── Core/                # ViewModelBase, PanelViewModelBase
│   ├── Shell/               # MainWindowViewModel
│   ├── Pages/               # Page view models + AssetsViewModel (workspace)
│   ├── ArchiveLoaders/      # Floating panel view models
│   ├── Sprites/             # SpriteViewModel, file-request args
│   ├── Things/              # Thing file-request args
│   └── Common/              # ArchiveFormat, shared enums/helpers
├── Services/
│   ├── Archive/             # SpriteLoader, compile (.spr/.assets ↔ .dat/.things)
│   ├── Persistence/         # settings.toml + app_state.toml
│   ├── Rendering/           # SpriteRenderer, thing preview compositor
│   ├── Exchange/            # nyx-thing / OBD import-export helpers
│   └── ImportExport/        # Sprite clipboard & image import
├── Models/                  # Lightweight domain models (SpriteModel)
└── docs/                    # Project documentation
```

## Navigation & views

Top-level pages use **`ViewLocator`** (`ViewModel` → `View` by naming convention). Archive panels use **`DataTemplate`** mappings in `AssetsView`. Details: [structure.md](structure.md#view--viewmodel-pairing).

## Workspace model

`AssetsViewModel` owns:

- **Dock columns** — left / center / right (`PanelViewModelBase` collections)
- **Floating panels** — canvas-positioned overlay
- **Archive pairing** — each things panel remembers its linked sprite panel; pending sprite loads are consumed by the next new things load

Compile operations require a matched pair (`.spr`+`.dat` or `.assets`+`.things`).

## Things panel sections

`FloatingThingsLoaderViewModel` filters the shared `ThingCatalog` by `ThingKind`:

| Tab | Kind |
|-----|------|
| Items | `ThingKind.Item` |
| Outfits | `ThingKind.Outfit` |
| Effects | `ThingKind.Effect` |
| Missiles | `ThingKind.Missile` |

Previews use `ThingPreviewRenderer` + `ThingFrameResolver` (south-facing outfits, frame 0 for items/effects, etc.). Outfit list previews draw **layer 0 only** (base sprite, no color mask).

## Persistence

| File | Contents |
|------|----------|
| `settings.toml` | Default page size, client version, ID offset, loader flags |
| `app_state.toml` | Panel positions, dock state, linked archive paths |

Both live next to the executable (`AppContext.BaseDirectory`).

## Thing editor

Double-click a thing in the Things Archive Viewer to open a floating **Thing Editor** panel.

| Tab | Contents |
|-----|----------|
| **Texture** | Appearance preview, outfit/missile direction controls, layer/frame/pattern sliders, grid & crop overlays, pattern dimensions, animation block, improved-animation durations |
| **Properties** | Common flags (stackable, rotatable, animate always, etc.) |

Edits apply live to the catalog. A linked sprite archive is required for texture previews.

## Looktype generator

The Assets toolbar can open a dockable Looktype Generator. It composes outfits or item-based
appearances from a selected linked archive pair without modifying either archive. Named profiles
are stored in the versioned `looktypes.json` file beside the executable and can be exchanged as
TFS-style Lua outfit tables or XML `<look>` fragments. The panel previews the selected profile's
generated text and accepts pasted single- or multi-looktype documents. File import reads every
valid entry; export writes the profile library as a valid multi-looktype document.
Generated Lua uses `creature.outfit` and `creature.corpse`; imports accept any table owner, such as
`monster.outfit` or `dragon.outfit`. XML uses the `corpse` attribute on `<look>` elements. The preview
can switch between the configured appearance and corpse item without changing the profile's exported
appearance mode.
Preview direction, animation timing, and rotation remain app-local by default. A per-profile opt-in
can preserve them in saved profiles and include importable Nyx metadata in Lua/XML exports.
Profile names can be included independently as quoted `nyx-looktype` metadata, keeping them out of
the TFS Lua outfit table and XML `<look>` attributes while still supporting multi-profile round trips.

Outfit previews use the NyxAssets frame resolver for direction, walking groups, addons, and mounts.
The renderer generates the 133 Tibia colours mathematically and applies the yellow/head, red/body,
green/legs, and blue/feet masks. Missing IDs or unsupported addon/mount patterns are reported inside
the panel and do not prevent the profile from being edited or moved to another archive pair.
