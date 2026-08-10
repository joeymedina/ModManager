# Mods Page Redesign

## Context

The Mods page had grown busy. Above the file list sat a folder path box, a disabled-folder
line, a search box, eight toolbar buttons, three view-mode radio buttons, and **four inline
panels** (install, adopt, add-to-group, delete confirmation) that appeared and disappeared in
place. Opening any of them pushed the list down and changed the page height.

This rework keeps every feature but moves each one somewhere it fits better. Nothing was
removed from the application or infrastructure layers; almost all of the change is in
`ModManager.Ui`.

Supersedes the UI descriptions in [mods-folder-ui.md](./mods-folder-ui.md) and the shell
description in [navigation-shell.md](./navigation-shell.md). Backend behaviour is unchanged —
see [mods-folder-service.md](./mods-folder-service.md).

## The one new dependency

`FluentAvaloniaUI` 3.0.2. It provides the modal dialog, the notification bar, the navigation
rail, and an icon set — all things Avalonia doesn't ship and that we would otherwise
hand-roll.

Note for anyone reading FluentAvalonia docs or samples online: **version 3 renamed every type
with an `FA` prefix.** What older material calls `ContentDialog`, `NavigationView`, `InfoBar`,
and `Symbol` are here `FAContentDialog`, `FANavigationView`, `FAInfoBar`, and `FASymbol`.

## What the page looks like now

Top to bottom, the page is five rows:

| Row | Contents |
| --- | --- |
| Header | "Mods" title, the current folder path underneath, Refresh, and **Install from file…** |
| Find bar | Search box, All / Enabled / Disabled chips, and the view-mode dropdown |
| Error bar | Hidden unless something failed |
| Content | File list on the left, details panel (320px) on the right |
| Status line | Quiet one-line summary, plus a progress bar while working |

The file list has a **selection bar** above it that only exists when files are selected. It
reads `3 selected` followed by Enable, Disable, Group…, and a More menu holding Adopt, Remove
from group, and Delete. The same actions are on the list's right-click menu.

Each row is `name → badges → on/off switch`, with the switch on the trailing edge.

## Design decisions

| Decision | Reason |
| --- | --- |
| Modal dialogs instead of inline panels | The page stops resizing as you work, and only one task is open at a time |
| Actions appear only when something is selected | Six of the eight old buttons did nothing without a selection, but looked available |
| A switch on every row | Turning one mod on or off is the most common action; it shouldn't require select-then-find-a-button |
| Switch on the right, not the left | A control in the leading position reads as a *selection* checkbox, and this list is multi-select |
| Folder path moved to a Settings page | It's set once and then just takes up space |
| Errors in a bar, counts in the status line | "Loaded 42 files" shouldn't demand attention; "3 files failed" should |
| Native file and folder pickers | Typing an archive path was the only way to install manually |

## Architecture

The layering is unchanged. The UI still reaches the filesystem only through
`IModsFolderUseCase` and `IArchiveInstallService`. Two new UI-only services sit beside the
page view model.

```text
┌──────────────────────────────────────────────────────────────────┐
│ UI                                                               │
│                                                                  │
│  MainWindow (FANavigationView)                                   │
│   └─ PageHost ─ ModsPageView / UpdatesPageView /                 │
│                 BrowsePageView / SettingsPageView                │
│                                                                  │
│  MainViewModel                                                   │
│   └─ ModsPageViewModel ── ModFileViewModel                       │
│        │                  ModTreeNodeViewModel                   │
│        │                  ModGroupNodeViewModel                  │
│        │                                                         │
│        ├─ IDialogService  (dialogs + OS file pickers)            │
│        └─ SettingsStore   (remembers the mods folder)            │
└───────────────────────────┬──────────────────────────────────────┘
                            │ IModsFolderUseCase, IArchiveInstallService
┌───────────────────────────▼──────────────────────────────────────┐
│ Application  →  Infrastructure    (unchanged)                    │
└──────────────────────────────────────────────────────────────────┘
```

### New types

| Type | Role |
| --- | --- |
| `IDialogService` / `DialogService` | Shows the three dialogs, asks for confirmation, opens OS file and folder pickers |
| `SettingsStore` / `AppSettings` | Reads and writes `settings.json` |
| `SettingsPageViewModel` / `SettingsPageView` | The Settings page |
| `InstallDialogContent`, `AdoptDialogContent`, `AddToGroupDialogContent` | The three dialog bodies |

## How the dialogs work

`DialogService` needs to build a view, but view models shouldn't know about view types. The
seam is a small enum:

```csharp
public enum ModsDialog { Install, Adopt, AddToGroup }

Task<bool> ShowAsync(string title, ModsDialog dialog, object dataContext, string primaryText);
```

The view model asks for a dialog by name and gets back `true` if the user confirmed. The
service maps the enum to a `UserControl` and hosts it in an `FAContentDialog`.

**The dialog bodies bind to `ModsPageViewModel` itself**, not to dedicated dialog view models.
The page already had `ArchivePathToInstall`, `InstallDisplayName`, `AdoptDisplayName`, and the
rest from the inline-panel design, so three new view models would have been three sets of
properties to copy values into and back out of. The dialogs are small forms whose results the
page immediately acts on; they don't own state worth isolating.

The trade-off: dialog content can't be resolved through `ViewLocator`, because that maps
`ModsPageViewModel` to `ModsPageView`. Hence the enum. If a dialog ever grows real logic of its
own, give it a view model and a matching `ShowAsync` overload.

### The install flow

Work stays in the view model; the dialog only gathers input.

1. **Install from file…** opens an OS file picker filtered to `.zip`, `.package`, `.ts4script`.
2. The chosen archive is previewed immediately — the dialog opens with its file list already filled in.
3. You untick files and set the folder name, then confirm.
4. The view model installs, reloads the list, and reports the result.

There is no Preview button. Choosing a file previews automatically; a hand-typed path
previews when you leave the box or press Enter. Not on every keystroke, because a preview
opens and walks the whole archive. `_previewedPath` tracks what was last read so blurring an
unchanged box does nothing.

Enter in the path box is swallowed deliberately — otherwise it reaches the dialog's Install
button and installs the *previous* preview instead of the path just typed.

Downloads from the Browse page enter the same flow through `BeginInstallFromFile`, which also
carries the source and mod-page URLs into the install record.

## Remembering the mods folder

`SettingsStore` reads and writes one file:

```
%APPDATA%\ModManager\settings.json      { "ModsFolderPath": "…" }
```

A missing or corrupt file falls back to defaults instead of failing startup, and a failed
write is swallowed — a preference that won't save is an annoyance, not a reason to lose the
session.

The Settings page and the Mods page must agree on the current folder, so **both view models
are registered as singletons.** `SettingsPageViewModel` calls
`ModsPageViewModel.SetModsFolderAsync`, which updates the path, saves it, and reloads. On
startup `MainViewModel` kicks off one refresh so the list is populated without pressing
Refresh.

## The file list

Three view modes share one area, each visible only in its mode:

| Mode | Control | Selection binding |
| --- | --- | --- |
| Flat | `ListBox` over `Files` | `SelectedFiles` |
| Folder | `TreeView` over `FolderTree` | `SelectedTreeNodes` → synced into `SelectedFiles` |
| Group | `TreeView` over `GroupTree` | `SelectedGroupNodes` → synced into `SelectedFiles` |

Selecting a folder or group node selects every file underneath it. All bulk actions read
`SelectedFiles` regardless of mode, so they don't care which view is showing.

### Filtering

`ApplyFilter` runs on search text or filter-chip change and rebuilds all three collections:

- **Search** matches anywhere in the relative path, so folder names match too.
- **Chips** narrow to Enabled or Disabled.
- **Group mode ignores the search box.** Groups are a small curated set you browse rather than hunt through, and a "missing" member has no file to match against. This predates the redesign.

### Flipping one row

`ToggleFileAsync` deliberately does **not** reload from disk. Enable and disable only move a
file between the two roots — the relative path, size, and every other field stay the same —
so it patches the single row:

```csharp
file.State = enabling ? Enabled : Disabled;
file.Refresh();
if (StatusFilter != All) ApplyFilter();   // the row may no longer belong in view
```

A full reload would rebuild every row and drop the selection out from under the click. On
failure the row is refreshed back to its real state, so the switch returns to where it was.

Bulk actions still reload from disk, because the repository may apply a batch partially and
local patching would be guesswork.

## What the UI tells you about a file

| State | Meaning | Toggleable |
| --- | --- | --- |
| Enabled | In the `Mods` folder | yes |
| Disabled | In the `Mods.Disabled` folder | yes |
| Conflicted | A copy exists in **both** folders | no — refused until resolved |

Conflicted is the one state a two-position switch can't show, so it keeps a caution-coloured
`Conflicted` label beside the switch. Script mods buried more than one folder deep get a
warning icon, since the game won't load them.

Both explanations are written **once**, in `UserControl.Resources`, and reused by the row
tooltips and the details-panel info bars so the wording can't drift. The matching failure
message in `ModsFolderService` is worded for the user, not the developer — it says what
happened and what to do about it, because it is shown verbatim in the error bar.

## Messages

| Kind | Where it goes | Example |
| --- | --- | --- |
| Routine | Status line at the page bottom | `329 file(s) — 329 enabled` |
| Failure | Dismissible `FAInfoBar` under the find bar | `1 file(s) failed: Foo.package: a copy exists in both…` |
| In progress | Indeterminate progress bar + `IsBusy` disables actions | `Installing…` |

## The shell

`MainWindow` uses `FANavigationView` with an icon rail. Nav items come from
`MainViewModel.NavigationItems`; each exposes an `FASymbolIconSource`.

| Page | Icon | Lifetime |
| --- | --- | --- |
| Mods | Folder | Singleton |
| Updates | Sync | Transient |
| Browse | Globe | Transient |
| Settings | Settings | Singleton |

One detail carried over from before and worth not breaking: **page views are built once and
kept alive**, hidden and shown inside a `Panel` rather than swapped through a
`ContentControl`. A content swap destroys and rebuilds the view each time you navigate away
and back, which reset page state — most visibly, the Browse page's embedded WebViews
reloaded from scratch. `MainWindow.axaml.cs` caches views in a dictionary and toggles
`IsVisible`.

## Design-time support

Every view model keeps a parameterless constructor for the XAML previewer. Two kinds of stub
back them, and they are not interchangeable:

- **`NoopDialogService`** — satisfies `IDialogService` by doing nothing and answering "user cancelled" (`false`, `null`). The real `DialogService` reaches for the main window, which doesn't exist at design time.
- **`DesignTimeModsFolderUseCase` / `DesignTimeArchiveInstallService`** — return a few plausible fake mod files so the preview isn't an empty list.

"Noop" means *does nothing*; "DesignTime" means *returns fake data*.

## Gotchas found while building this

Recorded because each one cost real debugging time and none is obvious from the code.

**Re-entrant popups crash the visual tree.** The group-name `AutoCompleteBox` forces its
dropdown open on focus. Picking a suggestion closes the dropdown and hands focus back to the
box, which re-fired the handler *during* the popup's teardown and mutated the visual-children
collection Avalonia was iterating — `ArgumentOutOfRangeException`, application terminated. The
fix opens the dropdown only when the box is still empty, and posts it through the dispatcher so
it can never run inside the close path.

**`IsFocused` is false on composite controls.** `AutoCompleteBox` focus lands on its inner
`TextBox`, so the outer control never reports focused. Use `IsKeyboardFocusWithin`.

**A binding through a null reference falls back to the property default, not to false.**
`IsVisible="{Binding File.IsConflicted}"` on a group *header* — which has no file — leaves
`IsVisible` at its default of `true`, labelling every group "Conflicted". Either guard the
path or set `FallbackValue=False`.

**Overlay scrollbars draw on top of row content.** Rows need right padding to clear them,
otherwise the scrollbar sits over the trailing column.

**`HorizontalAlignment="Left"` makes a control size to its content.** On a `TextBox` that means
it grows and shrinks as you type. Let it fill its column instead.

## Files touched

**Added**

- `ModManager.Ui/Services/IDialogService.cs`, `DialogService.cs`
- `ModManager.Ui/Services/SettingsStore.cs`
- `ModManager.Ui/ViewModels/SettingsPageViewModel.cs`
- `ModManager.Ui/Views/SettingsPageView.axaml` / `.cs`
- `ModManager.Ui/Views/Dialogs/` — three dialog contents
- `docs/architecture/mods-page-redesign.md` (this document)

**Updated**

- `ModManager.Ui/Views/ModsPageView.axaml` / `.cs` — rewritten
- `ModManager.Ui/ViewModels/ModsPageViewModel.cs` — dialogs replace panel flags; filter, toggle, detail, and error state added
- `ModManager.Ui/ViewModels/ModFileViewModel.cs` — display formatting (`SizeText`, `ModifiedText`), `IsEnabled`, `ModPageUrl`
- `ModManager.Ui/ViewModels/MainViewModel.cs`, `NavigationItemViewModel.cs` — Settings page, icons, startup refresh
- `ModManager.Ui/Views/MainWindow.axaml`, `App.axaml` — navigation view and theme
- `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`, `ModManager.Ui.csproj`
- `ModManager.Application/Models/ModFile.cs` — added `ModPageUrl`
- `ModManager.Infrastructure/Services/ModsFolderService.cs` — projects `ModPageUrl`; reworded the conflict message

## Out of scope

- Dropping Flat view. Folder view already shows root files at depth 0, so Flat is arguably redundant — deferred deliberately.
- Disabling the switch on conflicted rows. Today you can flip it, the move is refused, and the error bar explains why. Honest but wasteful.
- Sortable columns; default order is still relative path.
- Persisting anything beyond the mods folder path (window size, last page, view mode).
- Undo. Delete is still permanent.

## Verification

- `dotnet build ModManager.slnx` — clean, no warnings.
- `dotnet test ModManager.slnx` — 36 passing.
- Manual: toggle a row in each view mode; multi-select and use every action; install from a
  file and from a Browse download; change the folder in Settings and confirm it survives a
  restart; put the same file in both roots and confirm the conflict shows and refuses to toggle.

If the output folder is locked by a running instance, build elsewhere with
`-p:ArtifactsPath=<dir>` rather than killing the app.
