# System Architecture & Service Graph

This document describes the high-level architecture, component decoupling, docking panel framework, and service layers in **Nyx Assets Editor**.

---

## 1. Architectural Principles

Nyx Assets Editor is built around three core architectural goals:

1. **Strict MVVM Decoupling**: View layer (`Views/`) is completely decoupled from business models and rendering services (`Services/`). Views handle AXAML layout, styles, and pragmatic input mechanics (drag, resize, dialogs).
2. **Unified Resource Loading**: Assembly assets are decoupled via `FileSystemAssetLoader` to enable zero-recompile graphics customization.
3. **High-Performance Sprite Pipeline**: Asset archives (`.spr`, `.assets`) and catalogs (`.dat`, `.things`) use asynchronous decoding, lazy item preview compositing (`SkiaSharp`), and virtualized list containers.

---

## 2. Service Dependency Graph

```
NyxAssetsEditor (Avalonia App)
├── Core/
│   ├── ViewLocator           --> Resolves ViewModel -> View
│   └── FileSystemAssetLoader --> Intercepts avares:// to disk Assets/
├── ViewModels/
│   ├── AssetsViewModel       --> Owns Dock Columns, Floating Windows & Archive Pairing
│   ├── FloatingThingsLoader  --> Filters ThingCatalog by Kind (Item, Outfit, Effect, Missile)
│   └── FloatingSpriteLoader  --> SpriteArchive Viewer & LRU bitmap cache
└── Services/
    ├── Archive/              --> ArchiveCompileService & SpriteLoader
    ├── Exchange/             --> ThingDocumentJsonCodec & ObdThingCodec
    ├── Rendering/            --> SpriteRenderer & ThingPreviewRenderer (SkiaSharp)
    ├── Persistence/          --> settings.toml & app_state.toml manager
    └── ImportExport/         --> Sprite clipboard & OxiPNG web exporter
```

---

## 3. Canvas Docking & Window Management

The main workspace (`AssetsView` / `AssetsViewModel`) manages a flexible canvas docking engine:

- **Dock Columns**: Left, Center, and Right columns host docked loader panels (`PanelViewModelBase`).
- **Floating Windows**: Floating panels rendered on a top-level canvas with z-indexing, title-bar drag mechanics, and resize handles.
- **Archive Pairing Policy**: `AssetsViewModel` maintains pairing state between loaded `FloatingThingsLoaderViewModel` and `FloatingSpriteLoaderViewModel` instances. When an archive is updated, linked preview renderers automatically receive invalidation events.

---

## 4. Persistence Architecture

Application state is persisted automatically to the executable directory (`AppContext.BaseDirectory`):

- **`settings.toml`**: Configures global user preferences (default page size, target client version, ID offsets, OTFI detection preferences).
- **`app_state.toml`**: Saves active session layout (docked/floating panel coordinates, linked archive file paths, last selected tab).
