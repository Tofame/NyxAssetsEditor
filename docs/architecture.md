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
