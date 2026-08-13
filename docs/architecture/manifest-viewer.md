# Manifest viewer

## Context

Users can now inspect — and, since a follow-up, directly edit — the `.modmanager.json` manifest
that lives inside their Mods folder, from the Settings page: a "Formatted" breakdown (files,
groups, install records) alongside the raw JSON text, toggled with two chip buttons in a dialog.
Until now the manifest was invisible — useful for understanding why a mod shows a certain display
name/group/category, or for diagnosing a manifest that got quarantined (see
`ModsManifestService.Quarantine` in [mods-folder-service.md](./mods-folder-service.md)) — and any
correction required opening the hidden JSON file in an external text editor and hoping it matched
the exact key casing (`SchemaVersion`, `RelativePath`, …) `System.Text.Json` expects by default.

Related documentation: [theming.md](./theming.md) for the Settings-page-card-plus-dialog pattern
this feature reuses, and [mods-folder-service.md](./mods-folder-service.md) for the manifest's
on-disk shape and load/save/quarantine behavior this feature reads from and, now, writes back to.

## Architecture

```text
┌────────────────────────────────────────────────────────────────────┐
│ SettingsPageView                                                    │
│  "Manifest" card → View manifest… button                            │
└───────────────────────────┬──────────────────────────────────────┘
                            │ DataContext
┌───────────────────────────▼──────────────────────────────────────┐
│ SettingsPageViewModel.ViewManifestCommand                           │
│  1. Load + show dialog (primaryText "Save")                         │
│  2. If Save clicked AND viewer.HasUnsavedChanges → ConfirmAsync      │
│     warning (isDestructive) → SaveManifestRawAsync → StatusMessage  │
│  3. On success, refresh ModsPageViewModel.RefreshCommand             │
└─────────────┬─────────────────────────────────────────────────────┘
              │ AppDialog.ManifestViewer (closes before step 2 runs —
              │ no dialog is ever shown while another is still open)
┌─────────────▼─────────────────────────────────────────────────────┐
│ ManifestViewerDialogContent                                         │
│  Chip toggle "Formatted" / "Raw JSON" (not TabControl — see Gotchas) │
│  Formatted: Files / Groups / Installs sections                      │
│  Raw JSON: editable TextBox + caution-colored warning banner        │
└─────────────┬─────────────────────────────────────────────────────┘
              │ DataContext
┌─────────────▼─────────────────────────────────────────────────────┐
│ ManifestViewerViewModel                                              │
│  ManifestPath, ManifestExists, RawJson (mutable), HasUnsavedChanges  │
│  Files/Groups/Installs: ObservableCollection<Manifest*Row>          │
│  Never writes anything itself — SettingsPageViewModel owns the save  │
└─────────────┬─────────────────────────────────────────────────────┘
              │ built from / saved through
┌─────────────▼─────────────────────────────────────────────────────┐
│ IModsFolderUseCase.LoadManifestAsync / ReadManifestRawAsync /        │
│                     SaveManifestRawAsync                            │
│  → IModsFolderRepository (ModsFolderService)                        │
│    → ModsManifestService.LoadAsync / ReadRawAsync /                 │
│                          TryParseRaw (static) / SaveAsync            │
└──────────────────────────────────────────────────────────────────┘
```

`ModsPageViewModel` already owns the current mods folder path and the `IModsFolderUseCase`
instance (it's a DI singleton shared with `SettingsPageViewModel`, same as
`DisabledModsFolderPath`), so all three use-case methods are exposed there rather than injecting
`IModsFolderUseCase` a second time into `SettingsPageViewModel`.

### New types

| Type | Role |
| --- | --- |
| `ManifestRawContent` (`ModManager.Application.Models`) | `(string Path, bool Exists, string? Json)` — the manifest file's raw text as it exists on disk, or `Json: null` if the file doesn't exist yet. |
| `ManifestFileRow`, `ManifestGroupRow`, `ManifestInstallRow` (`ModManager.Ui.Models`) | Display-flattened rows: `ManifestFileRow` resolves a file's `GroupId` to the group's display name so the view never binds raw ids. |
| `ManifestViewerTab` (`ModManager.Ui.ViewModels`) | `Formatted` / `Raw` — drives which section is visible, toggled by chip buttons. |
| `ManifestViewerViewModel` | Holds a mutable `RawJson` and a `HasUnsavedChanges` flag (compares against the text captured at open time). Never writes to disk itself — it's a draft the caller reads back after the dialog closes, same division of responsibility as `ThemeEditorViewModel`/`SettingsPageViewModel` for theme edits. |
| `ManifestViewerDialogContent` | The dialog body: two `ToggleButton`s (`Classes="chip"`, the same pattern `ModsPageView`'s status filter uses) switching between the Formatted sections and an editable, monospace, non-wrapping raw `TextBox` with a caution-colored warning banner above it. |

### Changed types

| Type | Change |
| --- | --- |
| `IModsFolderRepository`, `IModsFolderUseCase`, `ModsFolderUseCase` | Gained `LoadManifestAsync`, `ReadManifestRawAsync`, `SaveManifestRawAsync`, following the existing thin-pass-through pattern every other method already uses. |
| `ModsFolderService` | Implements the three new repository methods. `SaveManifestRawAsync` calls `ModsManifestService.TryParseRaw` first and only reaches `SaveAsync` on success. |
| `ModsManifestService` | `GetManifestPath` made `public static` (was `private static`); gained `ReadRawAsync` (reads exact on-disk bytes, no round-trip through `JsonSerializer`) and `TryParseRaw` (static; parses+validates edited text with the same acceptance rules `LoadAsync` already applies — must deserialize, must not be an older schema — without touching disk). |
| `WickedWhimsUpdateStrategy.DeleteStaleFiles` | Gained the same path-containment guard `ExtractArchive` (in the same file) already had for zip-slip. See "Gotchas" — this feature is what made the gap worth closing. |
| `ModsPageViewModel` | Gained `LoadManifestAsync()` / `ReadManifestRawAsync()` / `SaveManifestRawAsync(rawJson)`, thin wrappers over its own `_modsFolderUseCase` + `ModsFolderPath` — the same proxy pattern `DisabledModsFolderPath` already uses for Settings-page consumption. |
| `AppDialog`, `DialogService` | Gained `ManifestViewer` → `ManifestViewerDialogContent`. `DialogService.ShowAsync`'s `FAContentDialog` also gained `MaxWidth = 900` as headroom (see Gotchas). |
| `SettingsPageViewModel` | `ViewManifestCommand` now drives the full show → confirm → save → refresh flow described above. |
| `SettingsPageView.axaml` | Gained a "Manifest" card with a single "View manifest…" button. |

## Design decisions

| Decision | Reason |
| --- | --- |
| Settings-page card + dialog, not a new nav page | Used rarely, same rationale as [theming.md](./theming.md)'s decision for the same shape — reuses `IDialogService` instead of a new page VM and nav entry |
| A dedicated `IModsFolderUseCase.*ManifestRaw*` surface, not exposing `ModsManifestService` directly to the UI | `ModsManifestService` is Infrastructure and has no interface; injecting it into `ModManager.Ui` would break the layering CLAUDE.md calls out ("Ui reaches the filesystem only through `IModsFolderUseCase`") |
| Raw text read via `File.ReadAllTextAsync`, not `JsonSerializer.Serialize(manifest)` | The raw tab should show exactly what's on disk, including any formatting quirks or a manifest currently flagged `UnreadableReason` — re-serializing the parsed model would silently "fix" a corrupt file's raw view |
| Close the dialog first (primary = "Save"), *then* confirm, *then* write | No dialog in this codebase is ever shown while another `FAContentDialog` is still open, and FluentAvalonia's `ContentDialog` (which `FAContentDialog` mirrors) doesn't support stacking them. Matches the existing `AddToGroupAsync`/`SetCategoryAsync` shape: dialog closes on the action button, the caller validates/acts afterward and reports through `StatusMessage`. |
| `SaveManifestRawAsync` parses via `TryParseRaw` and only then calls the existing `SaveAsync` | Reuses `SaveAsync`'s `UnreadableReason` guard and identical `WriteIndented` formatting on write, instead of writing the user's raw bytes verbatim — the file is always renormalized on save, which is a feature (fixes any stray formatting) not a bug |
| `HasUnsavedChanges` gates the confirm+save path, not the dialog's primary button label | Users who open the viewer just to look and dismiss with the same button used for saving shouldn't get a "Save changes?" prompt for edits they didn't make |
| Persistent warning banner *and* a separate `ConfirmAsync(isDestructive: true)` step | One passive warning is easy to skim past for something this consequential; the confirm step defaults its focus to Cancel (same protection `DeleteThemeAsync` uses) so a stray Enter can't confirm a bad edit |
| `ManifestFileRow.GroupName` resolves `GroupId` → group name in the view model, not in XAML | Keeps the binding a plain string property instead of a converter that needs the whole `Groups` collection in scope |

## Gotchas found while building this

**Avalonia's stock `TabControl` rendered broken inside `FAContentDialog`.** The first version of
this dialog used `TabControl`/`TabItem` for Formatted/Raw — nothing else in this codebase had ever
used it. Inside the dialog it rendered with an oversized, overlapping tab header and, combined with
a separate width-negotiation issue (below), unreadable clipped text. Rather than debug an unfamiliar
control blind (no Windows machine available to inspect live), it was replaced with the `ToggleButton
Classes="chip"` pattern already proven elsewhere (`ModsPageView`'s status filter), bound to a
`ManifestViewerTab` enum the same way `ModsPageViewModel.StatusFilter` already works.

**Dialog content wider than `ThemeEditorDialogContent`'s proven 440px consistently clipped, no
matter what caused it.** Several fixes were tried in sequence — raising `FAContentDialog.MaxWidth`
(FluentAvalonia's default `ContentDialogMaxWidth` is ~548px, matching WinUI, and doesn't grow to fit
wider content on its own), replacing `TabControl`, switching the raw-text control between `Wrap`/
`NoWrap` — and the clipping persisted identically across all of them, including on plain `TextBlock`s
in a simple `ItemsControl` with nothing exotic involved. That ruled out each individual suspect one
at a time. The width was never conclusively root-caused; the fix that actually worked was pulling
`ManifestViewerDialogContent`'s `Width` back down to 480px, matching the one dialog already known to
render correctly (`ThemeEditorDialogContent` at 440px). `DialogService`'s `MaxWidth = 900` was kept
as headroom for a future dialog that genuinely needs more room, not because any current dialog needs
it — see the comment at that call site.

**Raw JSON wrapping destroys its own indentation.** `ModsManifestService` writes manifests with
`WriteIndented = true`, so the file is already nicely formatted on disk. A `TextBox` with
`TextWrapping="Wrap"` breaks long values mid-line, which visually misaligns the indentation and
reads as a wall of text. Switched to `NoWrap` with the `TextBox`'s own horizontal+vertical scroll
(not nested in an outer `ScrollViewer`, which caused a separate double-scroller conflict) so each
line keeps the exact indentation it has on disk.

**Editing the manifest directly exposed an unguarded delete path already latent in
`WickedWhimsUpdateStrategy`.** `DeleteStaleFiles` built `Path.Combine(installRoot,
staleFile.RelativePath)` and called `File.Delete` on it with no containment check — unlike its
sibling `ExtractArchive` in the very same file, which already guards against zip-slip via
`Path.GetFullPath(...).StartsWith(root...)`. Before this feature, getting an attacker-chosen
`RelativePath` into an install record required hand-editing the hidden `.modmanager.json` outside
the app. This feature turns that into a one-dialog, in-app action (paste a rooted or `../`-escaping
`RelativePath` under a `"provider": "wickedwhims"` install record, save, and the next WickedWhims
update deletes whatever's at that path — anywhere the process can reach, not just the Mods folder).
Fixed by giving `DeleteStaleFiles` the same containment check `ExtractArchive` already has,
skip-and-log instead of throwing (it's a per-file cleanup loop, not a hard failure of the whole
update — matches this codebase's existing "continue past per-file errors" philosophy).

**`ModsManifestService`'s `JsonSerializerOptions` has no naming policy, so raw JSON is
case-sensitive PascalCase.** No `PropertyNamingPolicy` is configured, so `System.Text.Json` matches
JSON keys to the record's C# property names as written (`SchemaVersion`, `RelativePath`,
`DisplayName`, …), not camelCase. This is invisible when only round-tripping through `SaveAsync`/
`LoadAsync`, but the first drafts of the raw-edit tests used camelCase JSON and failed to parse —
worth knowing before hand-typing manifest JSON, since a user typing lowercase keys in the raw
editor gets a "not a valid manifest" rejection that might look like a bug at a glance.

**Optional install version in the "Installs" row.** A first draft used an Avalonia `MultiBinding`
with per-binding `StringFormat` to build "N file(s) · installed date · vX" in XAML; a null
`Version` risks the format literal (" · v") still rendering. Moved the string composition into a
computed `ManifestInstallRow.Summary` property instead — plain C# conditional, no binding-time
ambiguity.

## Known limitation

A rejected raw-JSON save (invalid JSON, or a schema version older than supported) discards the
user's typed/pasted edit — the dialog has already closed by the time validation runs (see the
"close first, confirm second" decision above), and reopening the viewer just shows the original
on-disk JSON again. Nothing is lost on disk (the write never happens), but the user has to redo
their edit from scratch. Matches how `AddToGroupAsync`'s blank-name rejection already works in this
app, and wasn't worth the added complexity of keeping a rejected draft around for this change, but
worth knowing before extending this pattern elsewhere.

## Files touched

**Added**

- `ModManager.Application/Models/ManifestRawContent.cs`
- `ModManager.Ui/Models/ManifestFileRow.cs`, `ManifestGroupRow.cs`, `ManifestInstallRow.cs`
- `ModManager.Ui/ViewModels/ManifestViewerViewModel.cs`
- `ModManager.Ui/Views/Dialogs/ManifestViewerDialogContent.axaml` / `.cs`
- `ModManager.Tests/Ui/ViewModels/ManifestViewerViewModelTests.cs`
- `docs/architecture/manifest-viewer.md` (this document)

**Updated**

- `ModManager.Application/Interfaces/IModsFolderRepository.cs`, `IModsFolderUseCase.cs`: new methods
- `ModManager.Application/Services/ModsFolderUseCase.cs`: pass-through implementations
- `ModManager.Infrastructure/Services/ModsFolderService.cs`, `ModsManifestService.cs`: new methods, `GetManifestPath` made public
- `ModManager.Infrastructure/Services/WickedWhims/WickedWhimsUpdateStrategy.cs`: `DeleteStaleFiles` containment guard
- `ModManager.Ui/ViewModels/ModsPageViewModel.cs`: proxy methods + `DesignTimeModsFolderUseCase` stub coverage
- `ModManager.Ui/ViewModels/SettingsPageViewModel.cs`: show → confirm → save → refresh flow
- `ModManager.Ui/Services/IDialogService.cs`, `DialogService.cs`: `AppDialog.ManifestViewer`, `MaxWidth` headroom
- `ModManager.Ui/Views/SettingsPageView.axaml`: Manifest card
- `ModManager.Tests/Application/Services/ModsFolderUseCaseTests.cs`, `ModManager.Tests/Infrastructure/Services/ModsFolderServiceTests.cs`, `ModManager.Tests/Infrastructure/Services/ModsManifestServiceTests.cs`, `ModManager.Tests/Ui/ViewModels/SettingsPageViewModelTests.cs`: new coverage

## Out of scope

- Live reload while the dialog is open
- Syntax highlighting or a real code-editor control for the raw JSON tab (no `AvaloniaEdit` dependency exists in this app; a monospace `TextBox` is enough for occasional edits)
- Keeping a rejected save's draft text around for retry (see "Known limitation" above)
- CLI manifest inspection or editing (a `--show-manifest`/`--edit-manifest` flag would be a separate, `ModManager.Cli`-only change)
- Full test coverage of `WickedWhimsUpdateStrategy` (it has none today — `internal` with no `InternalsVisibleTo` grant from `ModManager.Infrastructure` to `ModManager.Tests` — flagged separately rather than folded into this change)

## Verification

- `dotnet build ModManager.slnx -p:ArtifactsPath=<dir>`: clean, no warnings.
- `dotnet test ModManager.slnx -p:ArtifactsPath=<dir>`: 198 passing. The 2 remaining failures
  (`BrowserTabViewModelTests`, `ModFileViewModelTests`) are pre-existing, unrelated
  Windows-path-separator assumptions that only fail on macOS — this repo's test suite is
  Windows-only per CI (`windows-latest`).
- **Not verified**: visually opening and editing through the dialog in the running app (no Windows
  machine available in this environment to run `ModManager.Ui`, a `WinExe` with a WebView2
  reference). The width/`TabControl` issues above were found and fixed from user-supplied
  screenshots across several iterations, not by running the app directly. Run `dotnet run --project
  ModManager.Ui`, open Settings → Manifest → View manifest…, and confirm both tabs render correctly,
  edits round-trip, and the confirm-and-save flow works against a real Mods folder before shipping.
