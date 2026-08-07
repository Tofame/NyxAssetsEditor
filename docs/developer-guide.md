# Developer & Contributor Guide

This guide provides technical context for developer contributors building, extending, or maintaining **Nyx Assets Editor**.

---

## 1. Environment & Build Setup

### Prerequisites

- **SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/)
- **IDE**: JetBrains Rider, Visual Studio 2022 17.10+, or Visual Studio Code (with C# Dev Kit)

### Building the Project

```bash
# Clone repository
git clone https://github.com/Tofame/NyxAssetsEditor.git
cd NyxAssetsEditor

# Build solution
dotnet build NyxAssetsEditor.sln

# Run desktop app
dotnet run --project NyxAssetsEditor.csproj
```

### Build Commands (Makefile)

A `Makefile` is included in the project root for common build tasks:

```bash
make build    # Runs dotnet build
make run      # Runs dotnet run --project NyxAssetsEditor.csproj
make publish  # Compiles single-file self-contained deployment
```

---

## 2. Asset Resolution (`FileSystemAssetLoader`)

Unlike traditional Avalonia applications that compile UI graphics as embedded assembly resources (`avares://`), Nyx Assets Editor copies assets directly to the build output directory (`Assets/` folder as `<Content>`).

### How it Works

- `Core/FileSystemAssetLoader.cs` implements Avalonia's `IAssetLoader` interface.
- At startup in `App.axaml.cs`, reflection replaces the default asset loader with `FileSystemAssetLoader`.
- Any XAML image URI referencing `avares://NyxAssetsEditor/Assets/...` is intercepted and loaded dynamically from `AppDomain.CurrentDomain.BaseDirectory/Assets/...`.
- **Benefit**: Keeps compiled binary sizes minimal and permits end-users to customize UI assets without recompiling the project.

---

## 3. Code & Formatting Conventions

Refer to global repository rules (`AGENTS.md`):

1. **Tab Indentation**: Always use **4-width tab indentation** for all C# files.
2. **Pointer Syntax**: Place the asterisk directly next to the type name (`Texture* texture`, NOT `Texture *texture`).
3. **MVVM Decoupling**:
   - `ViewModels/` must NOT import `Avalonia.Controls` types (except image bitmap properties used purely as previews).
   - View navigation uses `MainWindowViewModel.CurrentPage` or explicit `DataTemplate` definitions.
4. **Compiled Bindings**: Ensure root views and item templates declare `x:DataType="..."` to enforce compile-time binding validation.

---

## 4. Checklist: Adding a New Feature Page

To add a new top-level page or tool to the editor:

1. **Create ViewModel**:
   - Add `ViewModels/Pages/MyFeatureViewModel.cs` inheriting from `ViewModelBase`.
2. **Create View**:
   - Add `Views/Pages/MyFeatureView.axaml` and `MyFeatureView.axaml.cs`.
   - Set `x:DataType="vm:MyFeatureViewModel"` in AXAML.
3. **Register Navigation**:
   - Add navigation command in `MainWindowViewModel.cs` (`NavigateToMyFeature`).
   - `ViewLocator.cs` automatically resolves `MyFeatureViewModel` to `MyFeatureView`.
4. **Service Isolation**:
   - Put heavy I/O, network, or decoding logic in a dedicated service under `Services/`.
