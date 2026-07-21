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

## Thing finder

Each loaded Things Viewer can open one dockable Thing Finder. Its filter form follows the
Thing Editor's Properties, Flags, Patterns, and JSON Custom Flags groupings. As in multi-edit mode,
each field has an explicit enable checkbox; unchecked fields do not create criteria, and boolean values
remain ordinary on/off switches. The form is rebuilt for Items, Outfits, Effects, or Missiles so
kind-specific editor fields only appear on relevant tabs. All
active fields must match. Clipboard copies use the configured displayed item offset; archive
integrations continue to use raw catalog IDs. Pattern criteria target the selected frame-group
index, while custom-flag filters are populated from extra properties discovered in the selected kind.
Results are paged using the same selectable page sizes as Things Viewer and can be displayed as either
preview tiles or a virtualized list without changing the active filters or result set.

Result context menus always provide **Copy ID**. When a Looktype Generator is open, compatible
results can also be assigned as its outfit, mount, or corpse. Assigning from another archive pair asks
before switching the generator. Finder itself remains independent: integrations are supplied through
generic context-action providers and Finder panels are not persisted between sessions.

## Looktype generator

The Assets toolbar can open a dockable Looktype Generator. It composes outfits or item-based
appearances from a selected linked archive pair without modifying either archive. Its single editable
Lua/XML field detects the format automatically: valid changes update the current controls and preview,
while changes made through the controls update the field. A Copy button places the current text on the
clipboard; the generator does not save a profile library or provide file import/export.
Generated Lua uses `creature.outfit` and `creature.corpse`; Lua input accepts any table owner, such as
`monster.outfit` or `dragon.outfit`. XML uses the `corpse` attribute on `<look>` elements. The preview
can switch between the configured appearance and corpse item without changing the configured
appearance mode.
Preview direction, animation timing, and rotation remain app-local by default. An opt-in includes
them as Nyx metadata in the Lua/XML field.

Outfit previews use the NyxAssets frame resolver for direction, walking groups, addons, and mounts.
The renderer generates the 133 Tibia colours mathematically and applies the yellow/head, red/body,
green/legs, and blue/feet masks. Missing IDs or unsupported addon/mount patterns are reported inside
the panel and do not prevent the profile from being edited or moved to another archive pair.
