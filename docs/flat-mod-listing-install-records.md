# Flat Mod Listing + Install Records

## Context

`ModsDiscoveryService.DerivePackageKey` guesses which files form a "package" by taking the
filename up to the first `_` or `-`. That guess is load-bearing: it is the merge key for
mod identity, so a rename mints a new `ModId` and silently orphans metadata; it invents
`IsMixedState` to paper over wrongly-merged files; and it forces `LoadModsAsync` to write
the manifest on every read.

Per `ModManager/docs/architecture/mod-listing-and-update-tracking.md`, we split the three
concerns the heuristic conflates: **discovery lists files**, **grouping is a user action**,
**updating is driven by install records**. This plan covers phases 1–3 of that doc
(flat listing, folder view, install pipeline + records) and outlines 4–5. **Phase 1 is
the current slice and ships on its own**; phases 2–3 are specified here but not started.

Decisions taken with the user:
- Manifest moves **into the Mods folder** so organization survives reinstall — but it
  arrives in **phase 3**, not phase 1. Nothing in phase 1 writes a manifest field
  (`DisplayName`, `GroupId` and `InstallId` are all filled by later phases), so phase 1
  deletes `ModsManifestService` outright and phase 3 reintroduces it in the new
  per-folder shape. Nothing is wasted; the shape is different anyway.
- The install pipeline gets built, including **install-from-file** for archives the
  in-app browser can't fetch.
- **Phase 1 ships alone.** It touches ~20 files and rewrites both mods test suites;
  phases 2 and 3 are easier to judge once the flat list is real.
- The phase-1 UI uses **multi-select + a bulk toolbar**, so the bulk repository API is
  exercised immediately rather than waiting for phase 5.
- **Delete is confirmed but still permanent.** No recycle bin — that is Windows-only via
  `Microsoft.VisualBasic.FileIO` and this repo supports macOS.

Two findings from exploration that shaped this:
- **No install pipeline exists.** `BrowserDownloadService` saves to `~/Downloads` and
  stops. The only archive→Mods code is the `internal`, WickedWhims-specific
  `WickedWhimsArchiveInstaller`. Phase 3 builds this, not just records.
- **The CLI is unaffected.** `--mod wickedwhims` maps to `IModUpdateStrategy.ModId`,
  unrelated to listing identity. `ModManager.Cli` needs no changes in phases 1–2.

---

## Phase 1 — Flat listing

### Models (`ModManager.Application/Models`)

Rename `ManagedModFile.cs` → `ModFile.cs` and extend it; this is the new main type.

```
ModFile(RelativePath, State, SizeBytes, ModifiedUtc, IsConflicted)
```

`RelativePath` (normalized, `/`-separated) is the identity — already stable across
enable/disable because `ModsFileOperationsService` preserves it when moving between roots.
`DisplayName` / `GroupId` / `InstallId` are deliberately absent: no phase-1 code writes
them, and the manifest that would hold them lands in phase 3.

Add one type for the bulk result:

```
ModFileFailure(RelativePath, Reason)
```

Delete outright: `ManagedMod.cs`, `Mods/ManifestMod.cs`, `Mods/ManifestModFile.cs`,
`Mods/ManifestProfile.cs`, `Mods/ManifestModel.cs`, and `Mods/RepositoryState.cs` —
with the manifest gone from the read path, `RepositoryState` has nothing left to carry
but the layout.

The per-folder manifest document (`ModsManifest(SchemaVersion, Files[], Groups[],
Installs[])`, sparse rows, empty on a fresh folder) is specified in phase 3 below, where
the first writer for it exists.

### `ModsDiscoveryService`

Becomes pure — no manifest argument, no writes. Delete `DerivePackageKey`, the grouping
`GroupBy`, and `ToManifestMod`. Keep `SupportedExtensions` and `EnumerateModFiles`.
`DiscoverFiles(layout)` returns a flat list sorted by `RelativePath`.

Enumerate with `DirectoryInfo.EnumerateFiles(..., AllDirectories)` rather than
`Directory.EnumerateFiles`: `SizeBytes` and `ModifiedUtc` come out of the directory walk
itself, instead of costing a second stat per file across tens of thousands of files.

**Conflict rule** (the doc's open question): a path present under *both* roots yields one
row with `IsConflicted = true` and `State = Enabled`. Enable/disable on a conflicted row
fails with a clear message; delete removes both copies. This is reachable today because
`MoveModFilesForStateChangeAsync` refuses to overwrite and leaves the source behind.

**Reads create nothing.** `LoadStateAsync` currently calls `Directory.CreateDirectory` on
both roots on every read, so listing someone's mods folder conjures a `Mods.Disabled`
next to it. Drop that; the disabled root is created by the first disable that needs it.

### `ModsManifestService`

**Deleted in phase 1**, along with the `APPDATA` redirection its tests needed. The old
`%APPDATA%/ModManager/mods-manifest.json` is left on disk, untouched and unread — nothing
in it is worth migrating (Guids and derived names).

Phase 3 reintroduces it against `Path.Combine(layout.ModsFolderPath, ".modmanager.json")`
— per folder, so organization survives a reinstall — with load/save taking the layout, no
profiles-keyed-by-path indirection, missing file → empty manifest, and any `SchemaVersion`
older than the current one ignored. `.json` is not a supported extension, so discovery
ignores the file and so does the game.

### `ModsFileOperationsService`

Signatures change from `ManagedMod` to `IReadOnlyList<ModFile>`, and both methods return
`IReadOnlyList<ModFileFailure>` instead of throwing on the first casualty. Bodies are
otherwise nearly unchanged — they already loop over `mod.Files`. Keep
`ResolveValidatedPath` and `RemoveEmptyDirectories` as-is.

**Partial failure**: a bulk move can fail partway — a file locked by the running game, or
an occupied destination (which is how conflicts get created in the first place). Attempt
every path, collect failures, return them. No rollback: undoing a half-applied batch of
file moves is itself a risky write, and the user can retry the failures. Conflicted rows
are skipped with a reason rather than aborting the batch.

### `ModsFolderService` / interfaces

`IModsFolderRepository` and `IModsFolderUseCase` become path-based and bulk-capable —
this is what makes phase-5 group operations free:

```
Task<IReadOnlyList<ModFile>> LoadFilesAsync(root, ct)                     // pure, no save
Task<IReadOnlyList<ModFileFailure>> EnableAsync (root, paths, ct)         // empty = all ok
Task<IReadOnlyList<ModFileFailure>> DisableAsync(root, paths, ct)
Task<IReadOnlyList<ModFileFailure>> DeleteAsync (root, paths, ct)
```

`LoadFilesAsync` is just discovery in phase 1 — no merge step until there is a manifest to
merge. The double-save in `SetModStateAsync` disappears with it, and so does the reload
that `SetModStateAsync` performs to return a refreshed mod; callers refresh instead.

### UI

- `ManagedModViewModel.cs` → `ModFileViewModel.cs`: `Name` (filename), `Folder`
  (relative dir), `Extension`, size, modified, state. No per-row commands — the actions
  move to the toolbar, which also keeps row templates cheap at scale.
- `ModsPageViewModel`: `Mods` → `Files`, plus `SearchText`. Search matches the **relative
  path**, not just the filename, so typing a folder name narrows to that folder.
- `ModsPageView.axaml`: `ListBox` gains `SelectionMode="Multiple"`; Enable / Disable /
  Delete become a toolbar acting on the selection. Drop the `PackageKey` / `ModId` /
  `FileCount` bindings. Details pane stays, showing one file's detail (name, folder,
  extension, size, modified, state, conflict warning) or count + total size for a
  multi-selection. Keep `ListBox` — it virtualizes, and thousands of rows are the normal
  case. **No new package**; `Avalonia.Controls.TreeDataGrid` is not referenced and isn't
  needed.
- **Delete confirmation** is an inline confirm bar in `ModsPageView` ("Delete 12 files
  permanently? [Delete] [Cancel]"), not a modal. Avalonia ships no message box and
  `ModManager.Ui.csproj` references no dialog package; an inline bar needs neither a new
  dependency nor a dialog-service seam through the view model.
- Status line reports partial failures: `"14 enabled, 2 failed: <reason>"`.
- Default sort is `RelativePath`. No sortable columns in phase 1.
- Update the `DesignTimeModsFolderUseCase` stub in `ModsPageViewModel`.

### Tests

`ModsFolderServiceTests` is filesystem-integration style (MSTest, real temp sandbox) and
mostly rewrites to paths instead of `ModId`. Its `APPDATA` redirection in `Initialize`
goes away with `ModsManifestService`. Add coverage for:
- a read writes **nothing** — no manifest, and no `Mods.Disabled` created,
- enable/disable round-trip preserving a nested relative path,
- the conflict row (same path seeded under both roots),
- partial failure: a batch with one bad path still applies the rest and reports the one.

`ModsFolderUseCaseTests` needs its Moq setups retyped; the assertions stand.

### Docs

`docs/architecture/mods-folder-service.md` and `docs/architecture/mods-folder-ui.md` both
document the `ManagedMod` / `PackageKey` model and go stale the moment this lands. Rewrite
both in the same PR.

---

## Phase 2 — Folder view

Group rows by `Path.GetDirectoryName(RelativePath)` in the view model behind a "Group by
folder" toggle; flat list stays the default. Avalonia's built-in `TreeView` covers it —
no dependency.

Also surface the `.ts4script` depth warning here: a script deeper than one level below
the Mods root will not load. Cheap, and users hit it constantly.

---

## Phase 3 — Install pipeline + records

Decisions taken with the user before starting:
- `ArchiveInstallService` gets an `IArchiveInstallService` interface (matches
  `IModsFolderRepository`'s precedent over `ModsDiscoveryService`'s bare-singleton one,
  since both the Mods page and the browser wiring call it and both want a mockable seam).
- `Install`/`Preview` return a result type (`ArchiveInstallResult` — success/error, no
  throw) instead of throwing, consistent with `ModFileFailure`'s non-throwing bulk-op
  style. Covers bad archives, non-zip (`.rar`/`.7z`), and extraction errors.
- `modFolderName` is a **sanitized display name** (illegal filesystem chars stripped),
  de-duped against an existing folder with a numeric suffix (`Foo`, `Foo (2)`, ...) —
  never a silent overwrite.
- The "Install to Mods" prompt hooks **both** `BrowserTabViewModel.DownloadAsync` and the
  WebView-native `OnBrowserDownloadUpdated` completion path. The spec originally named
  only `DownloadAsync`, but that path is the direct-link fallback — most real
  browser-initiated downloads complete through `OnBrowserDownloadUpdated`, so wiring only
  the first would leave most downloads without an install prompt.
- Bug (b) below ("updating a disabled mod silently re-enables it") is framing for a new
  requirement, not a live regression: the current CLI-only `WickedWhimsUpdateStrategy` has
  no disabled-root concept at all today, so there's nothing to "undo" — `ArchiveInstallService`
  just needs to build that awareness in from the start.

### Manifest (deferred from phase 1)

Reintroduce `ModsManifestService` here — the install record is its first writer. Per
folder at `Path.Combine(layout.ModsFolderPath, ".modmanager.json")`:

```
ModsManifest(SchemaVersion, Files[], Groups[], Installs[])
  ManifestFileEntry(RelativePath, DisplayName?, GroupId?, Notes?)   // sparse
  ModGroup(GroupId, Name, Members[])                                // phase 5
  InstallRecord(...)                                                // below
```

Sparse: a file with no user metadata gets no row; a fresh folder yields an empty manifest.
`LoadFilesAsync` gains a merge step that layers these rows onto the discovered files, and
`ModFile` gains `DisplayName?` / `GroupId?` / `InstallId?` at the same time. Reads still
never write.

### Install record

```
InstallRecord(InstallId, Source{Provider, ModPageUrl, DownloadUrl}, Version?,
              InstalledUtc, SourceArchivePath?,
              Files[{RelativePath, Sha256, SizeBytes}],
              SkippedEntries[])
```

### `ArchiveInstallService` (new, Infrastructure)

Replaces `WickedWhimsArchiveInstaller` (delete it — its path-traversal guard, the
`Path.GetFullPath` + prefix-check in `InstallArchive`, is worth keeping and carrying
over). Registered via a new `IArchiveInstallService` interface (`AddSingleton<IArchiveInstallService, ArchiveInstallService>()`
alongside the `ModsFolder*` block). Two methods, both returning a result type instead of
throwing:

```
ArchiveInstallResult<T>(bool Success, T? Value, string? Error)
```

so a bad archive, a non-zip (`.rar`/`.7z`), or an extraction failure surfaces as
`Error` for the UI to show inline, not an exception the caller has to catch.

**`Preview(archivePath)`** — this is the answer to "what about non-mod files in the
archive". Returns one row per entry, classified:
- `.package` / `.ts4script` → installable, **selected by default**
- everything else (readme, pdf, jpg, psd) → listed, **not installable**
- entries under a folder matching `optional|alternate|extras?` or sibling `.package`
  files with the same stem → installable but **deselected by default**, flagged as a
  variant

That last case is the one that matters: extracting every `.package` from an archive with
an `Optional/` folder installs conflicting variants and breaks the game. It cannot be
decided automatically, so the preview makes it a one-click user decision, defaulting to
the safe subset. The same screen is reused for phase 4 adoption.

**`Install(archivePath, selection, layout, displayName)`**
- `displayName` is sanitized into a filesystem-safe folder name (illegal chars stripped);
  if `Mods/<name>` already exists, de-dupe with a numeric suffix (`Foo`, `Foo (2)`, ...)
  rather than overwriting whatever's already there.
- Extracts selected entries under `Mods/<modFolderName>/` — one folder per mod, which is
  what makes phase 2's folder view meaningful for anything we install.
- Any `.ts4script` that would land deeper than one level is **flattened to the mod folder
  root**, since the game won't load it otherwise. Record the remapped path.
- Hashes each written file; returns an `InstallRecord`.
- Bare `.package` / `.ts4script` downloads (no archive) install as a single file.
- Non-zip archives (`.rar`, `.7z`) return `ArchiveInstallResult.Error` = "extract manually,
  then use Install from file" — `System.IO.Compression` is zip-only. Mark with a
  `ponytail:` comment naming SharpCompress as the upgrade path.

Non-mod entries are **not** extracted anywhere; they are recorded in `SkippedEntries` and
the UI points at `SourceArchivePath` for the readme. (If we later want them on disk,
extracting to `%APPDATA%/ModManager/extras/<InstallId>/` is a small addition.)

### Update path — the three bugs

- `WickedWhimsUpdateStrategy` reads the previous `InstallRecord`, installs the new
  version, then **deletes record paths absent from the new install**. This is the
  stale-file bug: version-stamped filenames currently leave both versions installed and
  the game loads both.
- Install targets the root the previous install's files were in, so **updating a disabled
  mod no longer silently re-enables it**.
- `WickedWhimsVersionDetector.FindInstalledVersion` scopes its scan to the record's file
  paths when a record exists. Today it `File.ReadAllBytes`-es every package in the tree —
  a non-starter on a 40 GB folder. Full scan stays only as the adoption fallback.

### Wiring

- `BrowserTabViewModel`: offer "Install to Mods" → preview → install after **both**
  `MarkCompleted` call sites — `DownloadAsync` (the direct-link fallback) and
  `OnBrowserDownloadUpdated` (the WebView-native path, where most real browser-initiated
  downloads actually complete). Wiring only `DownloadAsync` would leave most downloads
  without an install prompt.
- Mods page: "Install from file" action for archives already in `~/Downloads`.
- Register `ArchiveInstallService` in `InfrastructureServiceRegistrations` alongside the
  existing `ModsFolder*` singletons.

---

## Phases 4–5 (outline only)

- **Adoption** — select existing rows → link to a source → write an `InstallRecord` with
  current paths and a user-confirmed version. This is what makes pre-existing files
  updatable, and it reuses the phase-3 preview UI. Plan properly once phase 3 lands.
- **Manual groups** — `ModGroup` is already in the manifest shape and the bulk path-based
  repository API means group operations are just `EnableAsync(root, group.Members)`.
  Needed only for flat legacy folders; defer, since adoption may cover enough.

---

## Verification

- `dotnet build ModManager/ModManager.slnx` and `dotnet test ModManager/ModManager.slnx`.
  Existing suites: `ModsFolderServiceTests` (real filesystem), `ModsFolderUseCaseTests`
  (Moq), `ModUpdateOrchestratorTests`.
- New infrastructure tests against a temp sandbox — **phase 1**: reads write nothing;
  conflicted path; nested-path enable/disable round trip; partial-failure reporting.
  **Phase 3**: archive install writes only selected entries and flattens a deep
  `.ts4script`; update deletes stale paths from the prior record.
- Manual, phase 1: run `ModManager.Ui`, point it at a scratch Mods folder with nested
  subfolders, confirm multi-select bulk enable/disable, the delete confirm bar, search by
  folder name, and that no `Mods.Disabled` appears from a plain refresh.
- Manual, phase 3: a deliberately mixed archive (packages + readme + an `Optional/`
  variant), confirm the preview classifies correctly and the folder view groups as
  expected.
- CLI regression: `dotnet run --project ModManager/ModManager.Cli -- --check` must still
  work unchanged after phase 1.
- Phases 1–2 shipped via `feature/flat-mod-listing` (merged). Phase 3 branches fresh off
  `main` as `feature/install-pipeline`, PR into `main`.
