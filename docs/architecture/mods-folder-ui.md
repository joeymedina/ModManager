# Mods Folder UI

## Context

The previous commit introduced filesystem-backed mod folder management (`IModsFolderUseCase` / `ModsFolderService`) with no consumer UI. This change adds an Avalonia UI surface that loads discovered mods and runs enable, disable, and delete through the existing application use case.

Related backend documentation: [mods-folder-service.md](./mods-folder-service.md).

## What Changed

### Replaced placeholder shell

- Removed the temporary temperature-converter content from `MainWindow`.
- `MainWindow` now hosts the mods page as the primary application surface.
- Window title/size updated for a mod-manager layout.

### Wired folder use case into the UI

- `ModPageViewModel` depends on `IModsFolderUseCase`.
- Runtime construction goes through DI (`App` -> `AddApplicationServices` / `AddInfrastructureServices` / `AddUiServices`).
- Design-time / parameterless constructors keep the Avalonia previewer working without a full service graph.

### Added mod management screen

`ModPage` provides:

1. Mods folder path input (default: `Documents\Mods`)
2. Refresh action to discover mods
3. Display of resolved disabled folder path (`Mods.Disabled` sibling)
4. Status messaging for load/action success and failures
5. List of discovered mods with Enable / Disable / Delete
6. Details pane for the selected mod (name, package key, id, status, file count)

### Supporting view models

- `MainViewModel` holds the current page (`ModPageViewModel`).
- `ModPageViewModel` owns folder path state, busy/status state, and the mods collection.
- `ManagedModViewModel` wraps a single `ManagedMod` for binding and per-row commands.

## Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│ UI (Avalonia + CommunityToolkit.Mvvm)                       │
│  MainWindow -> ModPage                                      │
│  MainViewModel -> ModPageViewModel -> ManagedModViewModel   │
└────────────────────────────┬────────────────────────────────┘
                             │ IModsFolderUseCase
┌────────────────────────────▼────────────────────────────────┐
│ Application                                                 │
│  ModsFolderUseCase                                          │
│  Models: ManagedMod, ManagedModFile, ModsFolderLayout, ...  │
└────────────────────────────┬────────────────────────────────┘
                             │ IModsFolderRepository
┌────────────────────────────▼────────────────────────────────┐
│ Infrastructure                                              │
│  ModsFolderService                                          │
│   ├─ ModsFolderPathService                                  │
│   ├─ ModsManifestService                                    │
│   ├─ ModsDiscoveryService                                   │
│   └─ ModsFileOperationsService                              │
└─────────────────────────────────────────────────────────────┘
```

### Layering rules preserved

| Layer | Responsibility in this change |
| --- | --- |
| **UI** | Presentation, commands, busy/status state, binding models |
| **Application** | Use-case contract and orchestration already existed; UI only consumes it |
| **Infrastructure** | Filesystem discovery/moves/deletes and manifest IO already existed |

The UI does **not** talk to infrastructure types directly. All folder operations go through `IModsFolderUseCase`.

### Dependency injection

Registered in `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`:

- `ModPageViewModel` (transient)
- `MainViewModel` (transient)

Already registered by lower layers:

- `IModsFolderUseCase` -> `ModsFolderUseCase` (Application)
- `IModsFolderRepository` -> `ModsFolderService` (Infrastructure)
- Path/manifest/discovery/file-operation helpers (Infrastructure)

`App.OnFrameworkInitializationCompleted` builds the service provider and assigns `MainViewModel` as the main window `DataContext`.

### MVVM structure

| Type | Role |
| --- | --- |
| `MainViewModel` | Shell VM; exposes `CurrentPage` |
| `ModPageViewModel` | Page VM; path, refresh, status, collection ownership, enable/disable/delete orchestration |
| `ManagedModViewModel` | Item VM; maps `ManagedMod` fields for binding and raises row commands |
| `ModPage` | View bound to `ModPageViewModel` |
| `MainWindow` | Hosts `ModPage` with `DataContext="{Binding CurrentPage}"` |

`ManagedMod` is an application record and is not ideal for direct two-way UI mutation. `ManagedModViewModel` copies display/action state and is refreshed after successful use-case calls.

### Command flow

**Refresh**

1. Validate mods folder path.
2. Resolve layout via `GetLayout` (updates disabled path display).
3. Call `LoadModsAsync`.
4. Replace `Mods` collection with new `ManagedModViewModel` instances.
5. Preserve selection by `ModId` when possible.

**Enable / Disable**

1. Call `EnableModAsync` / `DisableModAsync` with folder path + `ModId`.
2. Apply returned `ManagedMod` onto the existing row VM.
3. Update status text / enable-disable affordances (`CanEnable` / `CanDisable`), including mixed-state mods.

**Delete**

1. Call `DeleteModAsync`.
2. Remove the row from the collection.
3. Clear selection if the deleted mod was selected.

All actions guard on `IsBusy` to avoid overlapping filesystem operations from the UI.

### Default path behavior

- Default mods path matches CLI defaults conceptually: `Environment.SpecialFolder.MyDocuments` + `Mods`.
- Disabled path is not entered by the user; it is derived by backend layout resolution (`Mods` -> sibling `Mods.Disabled`).

### Design-time support

`ModPageViewModel()` and `MainViewModel()` parameterless constructors exist for the XAML designer. The design-time path uses a private stub `IModsFolderUseCase` that returns sample mods and does not touch disk.

## Files Touched

### Added

- `ModManager.Ui/ViewModels/ManagedModViewModel.cs`
- `docs/architecture/mods-folder-ui.md` (this document)

### Updated

- `ModManager.Ui/ViewModels/ModPageViewModel.cs`
- `ModManager.Ui/ViewModels/MainViewModel.cs`
- `ModManager.Ui/Views/ModPage.axaml`
- `ModManager.Ui/Views/MainWindow.axaml`
- `ModManager.Ui/Views/MainWindow.axaml.cs`
- `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`
- `ModManager.Ui/ModManager.Ui.csproj` (`ImplicitUsings` enabled)

## Out of Scope

- Folder picker dialog / browse button
- Confirmation dialog before delete
- Per-file expansion inside a mod
- Update/download orchestration UI (`IModUpdateOrchestrator`)
- Multi-profile management UI
- Persistence of last-used mods folder path in app settings

## Verification

- `dotnet build ModManager.Ui/ModManager.Ui.csproj` succeeds.
- Manual check path: launch UI, set mods folder, Refresh, then Enable/Disable/Delete against a test mods directory.
