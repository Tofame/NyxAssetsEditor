# Project structure & MVVM

Nyx Assets Editor is an **Avalonia MVVM** desktop app. The folder layout does not replace MVVM — it **organizes** the same three layers Avalonia expects. Subfolders group related screens and services; they are not extra architectural tiers.

## MVVM layers (what goes where)

| Layer | Folder | Responsibility | Must not |
|-------|--------|----------------|----------|
| **View** | `Views/` | AXAML markup, styles, compiled bindings, minimal code-behind for pure UI mechanics | Hold business rules, parse archives, mutate catalogs |
| **ViewModel** | `ViewModels/` | Presentation state, commands, collections bound to UI, orchestration | Reference `Avalonia.Controls` types (except media bitmaps used as preview properties) |
| **Model / Services** | `Models/`, `Services/` | Domain data, file I/O, NyxAssets integration, rendering helpers | Reference Views or ViewModels (except persistence restoring into VMs) |

**Core/** holds app bootstrap (`App`, `Program`) and **`ViewLocator`** — Avalonia’s standard way to resolve `ViewModel` → `View` at runtime.

---

## View ↔ ViewModel pairing

Avalonia MVVM requires every screen to have a matching pair. This project uses **two resolution strategies**:

### 1. ViewLocator (top-level pages)

Registered in `Core/App.axaml`:

```xml
<Application.DataTemplates>
    <local:ViewLocator/>
</Application.DataTemplates>
```

Convention: replace `ViewModel` with `View` in the full type name.

| ViewModel | View |
|-----------|------|
| `NyxAssetsEditor.ViewModels.Shell.MainWindowViewModel` | `NyxAssetsEditor.Views.Shell.MainWindow` |
| `NyxAssetsEditor.ViewModels.Pages.HomeViewModel` | `NyxAssetsEditor.Views.Pages.HomeView` |
| `NyxAssetsEditor.ViewModels.Pages.SettingsViewModel` | `NyxAssetsEditor.Views.Pages.SettingsView` |
| `NyxAssetsEditor.ViewModels.Pages.AssetsViewModel` | `NyxAssetsEditor.Views.Pages.AssetsView` |

`MainWindow` binds navigation to `CurrentPage`; the locator picks the correct page view automatically.

### 2. Explicit DataTemplates (archive panels)

Sprite/things panels are **nested** inside `AssetsView`, not top-level routes. Their templates are declared on the workspace view:

```xml
<DataTemplate DataType="{x:Type panels:FloatingSpriteLoaderViewModel}">
    <loaders:FloatingSpriteLoaderControl />
</DataTemplate>
```

This is normal Avalonia MVVM: parent view hosts child view models via `ItemsControl` / `ContentControl` + `DataTemplate`.

---

## Folder map (MVVM-aware)

```
Core/
  App.axaml(.cs)          Application, themes, ViewLocator registration
  Program.cs               Entry point
  ViewLocator.cs           IDataTemplate: ViewModelBase → matching View

Views/
  Shell/                   MainWindow — chrome, nav buttons → commands
  Pages/                   Home, Settings, Assets workspace
  ArchiveLoaders/          FloatingSpriteLoaderControl, FloatingThingsLoaderControl

ViewModels/
  Core/                    ViewModelBase, PanelViewModelBase (shared panel chrome state)
  Shell/                   MainWindowViewModel — CurrentPage, Navigate* commands
  Pages/                   HomeViewModel, SettingsViewModel, AssetsViewModel
  ArchiveLoaders/          FloatingSpriteLoaderViewModel, FloatingThingsLoaderViewModel
                           ThingItemViewModel (row/tile VM for things list)
  Sprites/                 SpriteViewModel (row/tile VM for sprite list)
  Things/                  ThingFileRequestEventArgs (VM-layer event payload)
  Common/                  ArchiveFormat, ArchiveFormatHelper (presentation helpers)

Services/                  No AXAML — injected/used by ViewModels & code-behind bridges
  Archive/                 SpriteLoader, ArchiveCompileService
  Persistence/             settings.toml / app_state.toml
  Rendering/               SpriteRenderer, ThingPreviewRenderer
  Exchange/                Thing import/export (nyx-thing, OBD)
  ImportExport/            Sprite clipboard, image import

Models/
  SpriteModel.cs           Constants / lightweight domain shape (32×32 sprite)
```

**Why subfolders under ViewModels/Views?**  
Avalonia samples often use flat `Views/` + `ViewModels/`. As the app grows, feature folders (`Pages`, `ArchiveLoaders`) keep pairs discoverable without changing the MVVM rules. Namespaces mirror folders so ViewLocator keeps working.

---

## Bindings & commands (MVVM in AXAML)

- Root views set `x:DataType` to their view model → **compiled bindings**.
- Lists use item VMs (`SpriteViewModel`, `ThingItemViewModel`) with `DataTemplate x:DataType`.
- User actions bind to `[RelayCommand]` methods on the VM; read-only text/images use `Mode=OneWay`.
- Panel docking, pagination, selection, and archive state live on **`Floating*LoaderViewModel`** / **`AssetsViewModel`**, not in AXAML.

---

## What code-behind is allowed to do

Pure MVVM would push every interaction through the ViewModel. Avalonia apps commonly keep **view-specific** code in code-behind. This project follows that pragmatic split:

| Code-behind responsibility | Example | ViewModel alternative (not used here) |
|----------------------------|---------|--------------------------------------|
| Drag, resize, dock hit-testing | `FloatingSpriteLoaderControl` title bar / resize handles | Custom `Behavior` or attached properties |
| Native file picker | `StorageProvider.OpenFilePickerAsync` | `Interaction` request / service interface |
| Pointer → SelectSprite / SelectThing | `OnSpritePointerPressed` calls `vm.SelectSprite(...)` | `EventToCommand` behavior |

**Rule applied:** code-behind may handle **input mechanics and platform dialogs**, then **delegates decisions** to the ViewModel (`SelectSprite`, `LoadArchiveAsync`, `RequestExportSprites`, etc.). It does not parse `.spr` / `.dat` files or modify catalogs directly — that stays in ViewModels + `Services/`.

`AssetsView.axaml.cs` only wires `CompileAsHandler` to show save dialogs — compile logic remains in `AssetsViewModel` + `ArchiveCompileService`.

---

## Object ownership & navigation flow

```
App
 └── MainWindow                    (View: Shell)
      DataContext = MainWindowViewModel
      └── ContentControl           Content = CurrentPage
           ├── HomeView            ↔ HomeViewModel
           ├── SettingsView        ↔ SettingsViewModel
           └── AssetsView           ↔ AssetsViewModel
                ├── ItemsControl   Left/Center/Right docked panels
                ├── ItemsControl   FloatingPanels (Canvas)
                └── DataTemplates  Floating*LoaderViewModel → Floating*LoaderControl
```

- **`AssetsViewModel`** owns panel collections and archive pairing policy.
- Each **`Floating*LoaderViewModel`** owns one archive’s UI state (page, selection, file path).
- **`SpriteViewModel` / `ThingItemViewModel`** are **child item VMs** for list rows/tiles (MVVM item pattern).

---

## Services vs ViewModels

| Concern | Location |
|---------|----------|
| Open/read/write archive bytes | `Services/Archive`, NyxAssets APIs |
| Bitmap conversion for previews | `Services/Rendering` |
| Import/export nyx-thing, OBD, PNG | `Services/Exchange`, `Services/ImportExport` |
| Which page of sprites/things to show | ViewModel (`PagedSprites`, `PagedThings`) |
| Multi-select, anchor, shift-range | ViewModel |
| “Can compile?” / linked spr↔dat | `AssetsViewModel` |

ViewModels call services; services do not push UI updates except where persistence restores state into existing VMs on startup.

---

## Adding a new screen (checklist)

1. Create `ViewModels/Pages/MyFeatureViewModel.cs` inheriting `ViewModelBase`.
2. Create `Views/Pages/MyFeatureView.axaml` + `.cs` with `x:DataType="vm:MyFeatureViewModel"`.
3. Add a `[RelayCommand] NavigateToMyFeature` on `MainWindowViewModel` (or parent VM).
4. ViewLocator resolves the pair automatically if namespaces follow `ViewModels.Pages.*` ↔ `Views.Pages.*`.
5. Put non-UI logic in `Services/` if it is reused or heavy; keep presentation state in the VM.

---

## Related docs

- [architecture.md](architecture.md) — workspace pairing, things sections, persistence files
- [avalonia-performance.md](avalonia-performance.md) — bindings, virtualization, lazy previews
