# Avalonia performance practices

This document describes the performance-oriented choices in Nyx Assets Editor and how they map to common Avalonia guidance.

## Compiled bindings (default)

The project sets `AvaloniaUseCompiledBindingsByDefault` in `NyxAssetsEditor.csproj`. All primary views declare `x:DataType` on the root and on item `DataTemplate`s so bindings are resolved at compile time instead of via reflection.

## One-way bindings for read-only UI

List and grid item templates use `Mode=OneWay` for:

- `TextBlock.Text` (IDs, filenames, counts)
- `Image.Source` (sprite / thing previews)
- `ItemsSource` on list controls

Two-way binding is reserved for editable controls (`TextBox`, `CheckBox`, `ComboBox` selection).

## List virtualization

**List view mode** for both archive panels uses an explicit `VirtualizingStackPanel`:

```xml
<ListBox.ItemsPanel>
  <ItemsPanelTemplate>
    <VirtualizingStackPanel/>
  </ItemsPanelTemplate>
</ListBox.ItemsPanel>
```

Grid view mode uses `WrapPanel` (not virtualized) but is paginated (50–300 items per page), keeping the live visual tree bounded.

## Lazy preview loading

| Type | Strategy |
|------|----------|
| `SpriteViewModel.Preview` | Lazy — decodes on first bind via `SpriteRenderer` |
| `ThingItemViewModel.PreviewImage` | Lazy — composited on first bind via `ThingPreviewRenderer` |

Page changes no longer pre-render every preview up front; only visible rows trigger decode/composite work.

## Async archive I/O

User-triggered archive opens run file read / parse on a background thread:

- `FloatingSpriteLoaderViewModel.LoadArchiveAsync`
- `FloatingThingsLoaderViewModel.LoadArchiveAsync`

UI updates happen after `await` on the UI thread. Startup restore still calls synchronous `LoadArchive()` (fire-and-forget async) so the shell appears immediately while archives load.

## Lean data templates

Item templates avoid converters and keep a flat structure: thumbnail + label (+ selection chrome). Context menus bind directly to item commands.

## Resource disposal

- `FloatingSpriteLoaderViewModel` implements `IDisposable` for renderer lifetime.
- `WriteableBitmap` previews are cached per visible item and invalidated on sprite link change or page refresh (`InvalidatePreview`).

## Future improvements

- Cross-page selection without rebuilding selection state on every page change
- Background preview queue with cancellation when scrolling quickly
- Grid view virtualization (custom panel) if page sizes grow beyond ~300
