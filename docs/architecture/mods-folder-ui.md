# Mods Folder UI

> **Superseded for anything UI-shaped.** The page was redesigned — see
> [mods-page-redesign.md](./mods-page-redesign.md). The inline panels, the always-visible
> bulk toolbar, and the in-page folder path box are gone. What remains accurate here is the
> layering, the two-seam split between `IModsFolderUseCase` and `IArchiveInstallService`, the
> command flow, the list-mode/selection model, and the browser download wiring.

## Context

This document originally described a UI over grouped `ManagedMod` "packages". The
flat-listing rework (see
[flat-mod-listing-install-records.md](../flat-mod-listing-install-records.md)) replaced
that model with a flat, per-file list and then built four more phases on top of it: a
folder-tree view, an install pipeline (archives + browser downloads), adoption of
pre-existing files, and manual groups. This document covers the final shape across all
five phases. Related backend documentation: [mods-folder-service.md](./mods-folder-service.md).

## What the page does

`ModsPageView` loads discovered mod **files** (not grouped mods) and runs bulk enable,
disable, delete, install, adopt, and group actions through the application use case
(`IModsFolderUseCase`) and a sibling archive-install seam (`IArchiveInstallService`).

1. Mods folder path input (default: `Documents\Electronic Arts\The Sims 4\Mods`)
2. Refresh action to discover files and reload the manifest's groups
3. Display of resolved disabled folder path (`Mods.Disabled` sibling)
4. Search box filtering the **flat** and **folder** views by relative path (group view is
   never filtered — it's a small, curated set of groups, not something you hunt through)
5. Three mutually-exclusive list modes: **Flat**, **By folder**, **By group**
6. Multi-select across all three modes, backed by one canonical `SelectedFiles` collection
7. A bulk toolbar: Enable / Disable / Delete / Install from file / Adopt selected / Add to
   group / Ungroup
8. Inline panels (no modal dialogs) for delete confirmation, install-from-file preview,
   adopt, and add-to-group
9. Status messaging for load/action success, including partial-failure summaries
10. Details pane for the current selection: single-file detail (including `Version` /
    `Installed` / `Source` when the file has an owning `InstallRecord`), or count + total
    size for many

### Supporting view models

- `MainViewModel` holds the current page (`ModsPageViewModel`) and also subscribes to
  `BrowsePageViewModel.InstallRequested` to route a completed download's "Install to Mods"
  click into the Mods page (see "Browser download wiring" below).
- `ModsPageViewModel` owns folder path state, busy/status state, search text, `ListMode`,
  the three list collections (`Files`, `FolderTree`, `GroupTree`) and their selections, and
  all four inline-panel states (delete confirm, install, adopt, add-to-group).
- `ModFileViewModel` wraps a single `ModFile` for binding, including the manifest-merged
  `DisplayName`/`GroupId`/`InstallId`/`Version`/`InstalledUtc`/`Provider` fields and the
  `.ts4script` depth-warning flag. No commands of its own — actions live on the page view
  model and act on the current selection.
- `ModTreeNodeViewModel` — folder-view node. Uniform shape: `File` is null for a folder,
  non-null for a leaf; `Children` recurses. Built fresh from the filtered file list on
  every `ApplyFilter()`.
- `ModGroupNodeViewModel` — group-view node, deliberately mirroring `ModTreeNodeViewModel`'s
  uniform-node shape rather than two separate types (a group header and a member leaf),
  which would have needed Avalonia's multi-template `DataTemplates` machinery instead of a
  single `TreeDataTemplate`. A leaf's `File` is null and `MissingPath` is set when a
  group's member path no longer resolves to a discovered file — rendered as
  `"<name> (missing)"` rather than dropped.
- `ArchiveEntryPreviewViewModel` — one row in the install-from-file preview list, wrapping
  an `ArchiveEntryPreview` with a mutable `IsSelected` for the checkbox.

## Architecture

```text
┌──────────────────────────────────────────────────────────────────────┐
│ UI (Avalonia + CommunityToolkit.Mvvm)                                │
│  MainWindow -> ModsPageView                                          │
│  MainViewModel -> ModsPageViewModel -> ModFileViewModel /             │
│                    ModTreeNodeViewModel / ModGroupNodeViewModel /      │
│                    ArchiveEntryPreviewViewModel                       │
└───────────────┬────────────────────────────────┬──────────────────────┘
                │ IModsFolderUseCase              │ IArchiveInstallService
┌───────────────▼──────────────┐   ┌──────────────▼──────────────────────┐
│ Application                  │   │ (Infrastructure directly — install   │
│  ModsFolderUseCase           │   │  preview/extraction isn't a          │
│  Models: ModFile, ModGroup,  │   │  use-case-layer concern the way      │
│  InstallRecord, ...          │   │  enable/disable/adopt/group are)     │
└───────────────┬──────────────┘   └──────────────┬────────────────────┘
                │ IModsFolderRepository            │
┌───────────────▼───────────────────────────────────▼───────────────────┐
│ Infrastructure                                                        │
│  ModsFolderService            ArchiveInstallService                   │
│   ├─ ModsFolderPathService     (Preview / Install, own path-traversal │
│   ├─ ModsDiscoveryService       guard, folder-name dedup)             │
│   ├─ ModsFileOperationsService                                       │
│   └─ ModsManifestService (shared with ArchiveInstallService)          │
└─────────────────────────────────────────────────────────────────────┘
```

`ModsPageViewModel` holds both `IModsFolderUseCase` and `IArchiveInstallService` directly —
the second seam exists because archive preview/extraction isn't shaped like the bulk
path-based repository API (`EnableAsync`/`AdoptAsync`/...), so it was kept as its own
interface rather than forced onto `IModsFolderRepository`. Adoption and groups, by
contrast, *do* fit that shape (they never touch an archive) and live on
`IModsFolderRepository`/`IModsFolderUseCase` instead.

### Layering rules preserved

| Layer | Responsibility in this change |
| --- | --- |
| **UI** | Presentation, commands, busy/status state, binding models |
| **Application** | Use-case contracts and orchestration |
| **Infrastructure** | Filesystem discovery/moves/deletes, manifest IO, archive extraction |

The UI does **not** talk to infrastructure types directly. All folder operations go
through `IModsFolderUseCase` or `IArchiveInstallService`.

### Dependency injection

Registered in `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`:

- `ModsPageViewModel` (**now singleton** — shared with the Settings page)
- `MainViewModel` (transient)
- `BrowsePageViewModel` (transient)

Already registered by lower layers:

- `IModsFolderUseCase` -> `ModsFolderUseCase` (Application)
- `IModsFolderRepository` -> `ModsFolderService` (Infrastructure)
- `IArchiveInstallService` -> `ArchiveInstallService` (Infrastructure)
- Path/discovery/file-operation/manifest helpers (Infrastructure)

`App.OnFrameworkInitializationCompleted` builds the service provider and assigns `MainViewModel` as the main window `DataContext`.

### MVVM structure

| Type | Role |
| --- | --- |
| `MainViewModel` | Shell VM; exposes `CurrentPage`; routes browser-download installs into the Mods page |
| `ModsPageViewModel` | Page VM; path, search, list mode, busy/status, the three list collections, all inline-panel state, bulk actions |
| `ModFileViewModel` | Row VM; maps `ModFile` fields (including manifest-merged ones) for binding, no commands |
| `ModTreeNodeViewModel` | Folder-view node; uniform folder/leaf shape via nullable `File` |
| `ModGroupNodeViewModel` | Group-view node; uniform group/leaf/missing shape via nullable `File`/`MissingPath` |
| `ArchiveEntryPreviewViewModel` | One archive-entry row in the install preview, with a togglable `IsSelected` |
| `ModsPageView` | View bound to `ModsPageViewModel` |
| `MainWindow` | Hosts `ModsPageView` with `DataContext="{Binding CurrentPage}"` |

### List modes and selection

`ModsListMode` (`Flat` / `Folder` / `Group`) replaces what was originally a single
`GroupByFolder` bool. Three `RadioButton`s drive it via `Command="{Binding SetListModeCommand}"`
+ `CommandParameter` rather than a two-way `IsChecked` binding — `IsChecked`'s own
TwoWay binding can't round-trip through the `ObjectConverters.Equal` (`Convert`-only)
converter used to light up the matching radio button, so the actual mode change happens
through the command instead, with `IsChecked` bound one-way for display.

All three views (`ListBox` for flat, one `TreeView` for folder, one `TreeView` for group)
are visible/hidden by the same `ListMode` equality binding and layered in one `Panel` so
they overlap without shifting layout. Each has its own selection collection
(`SelectedFiles`, `SelectedTreeNodes`, `SelectedGroupNodes`); selecting a folder or group
node flattens to the underlying `ModFileViewModel`s (skipping folder headers and missing
group members) and writes them into `SelectedFiles`, which is the **one** collection every
bulk action reads. This means Enable/Disable/Delete/Adopt/Add-to-group/Ungroup work
identically no matter which view mode is active.

### Command flow

**Refresh**

1. Validate mods folder path.
2. Resolve layout via `GetLayout` (updates disabled path display).
3. Call `LoadFilesAsync` and `LoadGroupsAsync`.
4. Rebuild `ExistingGroupNames` (for the add-to-group autocomplete).
5. Replace the backing file list, rebuild the folder tree and the group tree, and
   re-apply the current search filter (folder tree only — group tree is never filtered).
6. Clear all three selections (stale row references would otherwise dangle).

**Enable / Disable / Delete** (bulk)

1. Collect `RelativePath` from every row in `SelectedFiles`; no-op with a status message if
   nothing is selected.
2. Call `EnableAsync` / `DisableAsync` / `DeleteAsync` with the folder path and the path list.
3. Refresh the file list from disk (no local patching — the repository may have partially
   applied the batch).
4. Report a summary: `"12 enabled, 2 failed: <reason>"` when there are failures, or `"14 enabled."` when there are none.

**Install from file**

1. "Install from file..." toggles an inline panel with an archive-path textbox.
2. "Preview" calls `IArchiveInstallService.PreviewAsync`, populates
   `ArchivePreviewEntries`, and defaults `InstallDisplayName` to the archive's filename.
3. The user reviews/adjusts checkboxes (variants and non-mod entries start deselected)
   and confirms; "Install" calls `InstallAsync` with the selected entry names, the typed
   display name, and an `InstallSource` — `Provider` is `"browser"` when the panel was
   opened via a completed download (see below) or `"manual"` otherwise.
4. On success, reloads the file list and reports `"Installed N file(s) to \"<name>\"."`.

**Adopt selected**

1. "Adopt selected..." requires a non-empty selection, then opens an inline panel:
   `DisplayName` (required), `Version` / `Mod page` (both optional).
2. "Adopt" calls `IModsFolderUseCase.AdoptAsync` with the selected paths — metadata only,
   nothing on disk moves.
3. On success, reloads and reports `"Adopted N file(s) as \"<name>\"."`.

**Add to group / Ungroup**

1. "Add to group..." requires a non-empty selection, then opens an inline panel with an
   `AutoCompleteBox` bound to `ExistingGroupNames` (`ItemsSource`) and `GroupNameInput`
   (`Text`), letting the user pick an existing group or type a new name.
   `MinimumPrefixLength="0"` alone doesn't open the dropdown on focus with empty text (it
   only re-populates on a `TextChanged` event), so `ModsPageView.axaml.cs` handles
   `GotFocus` and forces `IsDropDownOpen = true` explicitly.
2. "Add" calls `AddToGroupAsync`; case-insensitive name match reuses an existing group.
3. "Ungroup" calls `RemoveFromGroupAsync` directly on the current selection (no panel —
   there's nothing to ask the user).

There is no rollback on partial failure — see the "Error and Conflict Behavior" section of
[mods-folder-service.md](./mods-folder-service.md).

### Browser download wiring

A completed download in the Browse page can jump straight into the install-from-file flow:

1. `DownloadItemViewModel.InstallToModsCommand` (shown once `State == Completed` and the
   file extension looks like a mod) raises `InstallRequested(filePath, sourceUri, modPageUri)`.
2. `BrowsePageViewModel` re-raises the same event; `MainViewModel` subscribes to it.
3. `MainViewModel.OnInstallRequested` switches `SelectedNavigationItem` to the Mods page
   and calls `ModsPageViewModel.BeginInstallFromFile(filePath, sourceUri, modPageUri)`,
   which pre-fills the archive path, opens the panel, and runs Preview automatically.
4. `sourceUri`/`modPageUri` are captured by `BrowserTabViewModel.OnBrowserDownloadStarted`
   from `AddressText` at the moment the native download is intercepted (the tab doesn't
   navigate away, so it's still the referring page) — this is what lets the eventual
   `InstallRecord.Source` carry both the direct download URL and the mod's page,
   distinct from each other. The direct-link fallback path (`DownloadAsync`) never sets
   `modPageUri`: there, the address bar's URL and the download's URL are the same thing,
   not a separate page.

### Delete confirmation

> **Out of date.** Delete now uses a real modal via `IDialogService.ConfirmAsync`, as do
> install, adopt, and add-to-group. The reasoning below — that no dialog package was
> available — no longer holds: FluentAvaloniaUI was added for exactly this. Deletion being
> permanent, with no recycle bin, is still true.

Delete is two-step: `RequestDeleteSelectedCommand` shows an inline confirmation bar
("Delete N file(s) permanently? [Delete] [Cancel]") rather than a modal dialog.
`ModManager.Ui.csproj` references no dialog/message-box package and Avalonia ships none by
default, so an inline bar avoids adding a dependency or a dialog-service seam through the
view model. Confirming calls the same bulk-delete path as above. Deletion is still
permanent — no recycle bin, since that would need a Windows-only API
(`Microsoft.VisualBasic.FileIO`) and this app also targets macOS.

### Default path behavior

- Default mods path: `Documents\Electronic Arts\The Sims 4\Mods` under
  `Environment.SpecialFolder.MyDocuments`.
- Disabled path is not entered by the user; it is derived by backend layout resolution (`Mods` -> sibling `Mods.Disabled`).

### Design-time support

> The design-time constructors now also take a `NoopDialogService` and a `SettingsStore`.

`ModsPageViewModel()` and `MainViewModel()` parameterless constructors exist for the XAML
designer. The design-time path uses private stub implementations of `IModsFolderUseCase`
and `IArchiveInstallService` that return sample data (including a nested `.ts4script` to
exercise the depth-warning rendering) and never touch disk.

## Verification

- `dotnet build ModManager.slnx` and `dotnet test ModManager.slnx` succeed.
- Manual check path: launch the UI, set a mods folder, Refresh, then exercise all three
  list modes, multi-select bulk Enable/Disable/Delete (including the confirm bar), install
  a test archive with a normal package + an `Optional/` variant + a deep `.ts4script`,
  adopt a pre-existing file, create/reuse a group by name (including via the
  `AutoCompleteBox` dropdown), delete a grouped file's underlying file on disk and confirm
  it renders as "(missing)" in the group view.
