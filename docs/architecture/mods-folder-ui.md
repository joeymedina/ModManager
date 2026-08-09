# Mods Folder UI

## Context

This document originally described a UI over grouped `ManagedMod` "packages". Phase 1 of
the flat-listing rework (see
[flat-mod-listing-install-records.md](../flat-mod-listing-install-records.md)) replaced
that model with a flat, per-file list, so this document is rewritten to match. Related
backend documentation: [mods-folder-service.md](./mods-folder-service.md).

## What the page does

`ModsPageView` loads discovered mod **files** (not grouped mods) and runs bulk enable,
disable, and delete through the existing application use case.

1. Mods folder path input (default: `Documents\Mods`)
2. Refresh action to discover files
3. Display of resolved disabled folder path (`Mods.Disabled` sibling)
4. Search box filtering the list by relative path (matches folder names too, not just filename)
5. Multi-select list of discovered files with a bulk Enable / Disable / Delete toolbar
6. Inline delete confirmation bar (no modal — see "Delete confirmation" below)
7. Status messaging for load/action success, including partial-failure summaries
8. Details pane for the current selection (single file detail, or count + total size for many)

### Supporting view models

- `MainViewModel` holds the current page (`ModsPageViewModel`).
- `ModsPageViewModel` owns folder path state, busy/status state, search text, the file
  collection, the selection, and delete-confirmation state.
- `ModFileViewModel` wraps a single `ModFile` for binding. It has no commands of its own —
  actions live on the page view model and act on the current selection.

## Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│ UI (Avalonia + CommunityToolkit.Mvvm)                       │
│  MainWindow -> ModsPageView                                 │
│  MainViewModel -> ModsPageViewModel -> ModFileViewModel      │
└────────────────────────────┬────────────────────────────────┘
                             │ IModsFolderUseCase
┌────────────────────────────▼────────────────────────────────┐
│ Application                                                 │
│  ModsFolderUseCase                                          │
│  Models: ModFile, ModFileFailure, ModsFolderLayout           │
└────────────────────────────┬────────────────────────────────┘
                             │ IModsFolderRepository
┌────────────────────────────▼────────────────────────────────┐
│ Infrastructure                                              │
│  ModsFolderService                                          │
│   ├─ ModsFolderPathService                                  │
│   ├─ ModsDiscoveryService                                   │
│   └─ ModsFileOperationsService                              │
└─────────────────────────────────────────────────────────────┘
```

### Layering rules preserved

| Layer | Responsibility in this change |
| --- | --- |
| **UI** | Presentation, commands, busy/status state, binding models |
| **Application** | Use-case contract and orchestration |
| **Infrastructure** | Filesystem discovery/moves/deletes |

The UI does **not** talk to infrastructure types directly. All folder operations go through `IModsFolderUseCase`.

### Dependency injection

Registered in `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`:

- `ModsPageViewModel` (transient)
- `MainViewModel` (transient)

Already registered by lower layers:

- `IModsFolderUseCase` -> `ModsFolderUseCase` (Application)
- `IModsFolderRepository` -> `ModsFolderService` (Infrastructure)
- Path/discovery/file-operation helpers (Infrastructure)

`App.OnFrameworkInitializationCompleted` builds the service provider and assigns `MainViewModel` as the main window `DataContext`.

### MVVM structure

| Type | Role |
| --- | --- |
| `MainViewModel` | Shell VM; exposes `CurrentPage` |
| `ModsPageViewModel` | Page VM; path, search, busy/status, file collection, selection, bulk actions, delete confirmation |
| `ModFileViewModel` | Row VM; maps `ModFile` fields for binding, no commands |
| `ModsPageView` | View bound to `ModsPageViewModel` |
| `MainWindow` | Hosts `ModsPageView` with `DataContext="{Binding CurrentPage}"` |

### Selection and bulk actions

The list (`ListBox`) uses `SelectionMode="Multiple"` bound two-way to
`ModsPageViewModel.SelectedFiles`. Enable, Disable, and Delete are toolbar buttons acting
on the current selection rather than per-row buttons — with thousands of rows the normal
case, per-row action buttons don't scale and the bulk repository API
(`EnableAsync`/`DisableAsync`/`DeleteAsync` over a path list) exists specifically to serve
this.

### Command flow

**Refresh**

1. Validate mods folder path.
2. Resolve layout via `GetLayout` (updates disabled path display).
3. Call `LoadFilesAsync`.
4. Replace the backing file list and re-apply the current search filter.
5. Clear the selection (stale row references would otherwise dangle).

**Enable / Disable / Delete** (bulk)

1. Collect `RelativePath` from every selected row; no-op with a status message if nothing is selected.
2. Call `EnableAsync` / `DisableAsync` / `DeleteAsync` with the folder path and the path list.
3. Refresh the file list from disk (no local patching — the repository may have partially applied the batch).
4. Report a summary: `"12 enabled, 2 failed: <reason>"` when there are failures, or `"14 enabled."` when there are none.

There is no rollback on partial failure — see the "Error and Conflict Behavior" section of
[mods-folder-service.md](./mods-folder-service.md).

### Delete confirmation

Delete is two-step: `RequestDeleteSelectedCommand` shows an inline confirmation bar
("Delete N file(s) permanently? [Delete] [Cancel]") rather than a modal dialog.
`ModManager.Ui.csproj` references no dialog/message-box package and Avalonia ships none by
default, so an inline bar avoids adding a dependency or a dialog-service seam through the
view model. Confirming calls the same bulk-delete path as above. Deletion is still
permanent — no recycle bin, since that would need a Windows-only API
(`Microsoft.VisualBasic.FileIO`) and this app also targets macOS.

### Default path behavior

- Default mods path matches CLI defaults conceptually: `Environment.SpecialFolder.MyDocuments` + `Mods`.
- Disabled path is not entered by the user; it is derived by backend layout resolution (`Mods` -> sibling `Mods.Disabled`).

### Design-time support

`ModsPageViewModel()` and `MainViewModel()` parameterless constructors exist for the XAML designer. The design-time path uses a private stub `IModsFolderUseCase` that returns sample files and does not touch disk.

## Files Touched (phase 1)

- `ModManager.Ui/ViewModels/ModFileViewModel.cs` (renamed from `ManagedModViewModel.cs`)
- `ModManager.Ui/ViewModels/ModsPageViewModel.cs`
- `ModManager.Ui/Views/ModsPageView.axaml`
- `docs/architecture/mods-folder-ui.md` (this document)

## Out of Scope (phase 1)

- Folder picker dialog / browse button
- Folder-tree grouping view (phase 2)
- `.ts4script` depth warning (phase 2)
- Sortable columns (default sort is `RelativePath`)
- Update/download orchestration UI (`IModUpdateOrchestrator`)
- Multi-profile management UI
- Persistence of last-used mods folder path in app settings
- A real modal delete confirmation (would need a dialog package/service)

## Verification

- `dotnet build ModManager.Ui/ModManager.Ui.csproj` succeeds.
- Manual check path: launch UI, set mods folder, Refresh, then multi-select
  Enable/Disable/Delete (including the confirm bar) against a test mods directory with
  nested subfolders; confirm search narrows by folder name too.
