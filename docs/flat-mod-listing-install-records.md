# Flat Mod Listing + Install Records

## Context

`ModsDiscoveryService.DerivePackageKey` guesses which files form a "package" by taking the
filename up to the first `_` or `-`. That guess is load-bearing: it is the merge key for
mod identity, so a rename mints a new `ModId` and silently orphans metadata; it invents
`IsMixedState` to paper over wrongly-merged files; and it forces `LoadModsAsync` to write
the manifest on every read.

Per `ModManager/docs/architecture/mod-listing-and-update-tracking.md`, we split the three
concerns the heuristic conflates: **discovery lists files**, **grouping is a user action**,
**updating is driven by install records**. This plan executes phases 1–3 of that doc
(flat listing, folder view, install pipeline + records) and outlines 4–5.

Decisions taken with the user:
- Manifest moves **into the Mods folder** so organization survives reinstall.
- The install pipeline gets built, including **install-from-file** for archives the
  in-app browser can't fetch.

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
ModFile(RelativePath, State, SizeBytes, ModifiedUtc,
        DisplayName?, GroupId?, InstallId?, IsConflicted)
```

`RelativePath` (normalized, `/`-separated) is the identity — already stable across
enable/disable because `ModsFileOperationsService` preserves it when moving between roots.

Delete: `ManagedMod.cs`, `Mods/ManifestMod.cs`, `Mods/ManifestModFile.cs`,
`Mods/ManifestProfile.cs`. Replace `Mods/ManifestModel.cs` with a per-folder document:

```
ModsManifest(SchemaVersion, Files[], Groups[], Installs[])
  ManifestFileEntry(RelativePath, DisplayName?, GroupId?, Notes?)   // sparse
  ModGroup(GroupId, Name, Members[])                                // phase 5
  InstallRecord(...)                                                // phase 3
```

Sparse: a file with no user metadata gets no row. Fresh folder → empty manifest.
`RepositoryState` drops `Profile`, becoming `(Layout, Manifest, Files)`.

### `ModsDiscoveryService`

Becomes pure — no manifest argument, no writes. Delete `DerivePackageKey`, the grouping
`GroupBy`, and `ToManifestMod`. Keep `SupportedExtensions` and `EnumerateModFiles`.
`DiscoverFiles(layout)` returns a flat list sorted by `RelativePath`.

**Conflict rule** (the doc's open question): a path present under *both* roots yields one
row with `IsConflicted = true` and `State = Enabled`. Enable/disable on a conflicted row
fails with a clear message; delete removes both copies. This is reachable today because
`MoveModFilesForStateChangeAsync` refuses to overwrite and leaves the source behind.

### `ModsManifestService`

Path becomes `Path.Combine(layout.ModsFolderPath, ".modmanager.json")` — per folder, not
`%APPDATA%`. Delete `GetOrCreateProfile` and the profiles-keyed-by-path indirection;
load/save take the layout. Missing file → empty manifest. Ignore any `SchemaVersion`
older than the new one. Leave the old AppData file on disk untouched and unread; nothing
in it is worth migrating (Guids and derived names). `.json` is not a supported extension,
so discovery already ignores it and the game does too.

### `ModsFileOperationsService`

Signatures change from `ManagedMod` to `IReadOnlyList<ModFile>`. Both methods already
loop over `mod.Files`, so the bodies are nearly unchanged. Keep `ResolveValidatedPath`
and `RemoveEmptyDirectories` as-is.

### `ModsFolderService` / interfaces

`IModsFolderRepository` and `IModsFolderUseCase` become path-based and bulk-capable —
this is what makes phase-5 group operations free:

```
Task<IReadOnlyList<ModFile>> LoadFilesAsync(root, ct)     // pure, no save
Task EnableAsync(root, IReadOnlyList<string> paths, ct)
Task DisableAsync(root, IReadOnlyList<string> paths, ct)
Task DeleteAsync(root, IReadOnlyList<string> paths, ct)
```

`LoadFilesAsync` = discover (pure) + merge manifest metadata onto the rows. The
double-save in `SetModStateAsync` disappears with it.

### UI

- `ManagedModViewModel.cs` → `ModFileViewModel.cs`: `Name` (filename), `Folder`
  (relative dir), `Extension`, size, modified, state.
- `ModsPageViewModel`: `Mods` → `Files`, plus `SearchText` filtering on filename.
- `ModsPageView.axaml`: drop the `PackageKey` / `ModId` / `FileCount` bindings. Keep
  `ListBox` — it virtualizes, and thousands of rows are the normal case. **No new
  package**; `Avalonia.Controls.TreeDataGrid` is not referenced and isn't needed.
- Update the `DesignTimeModsFolderUseCase` stub in `ModsPageViewModel`.

### Tests

`ModsFolderServiceTests` is filesystem-integration style and mostly rewrites to paths
instead of `ModId`. Its `APPDATA` redirection in `Initialize` can go, since the manifest
now lives in the sandbox Mods folder. Add coverage for:
- a read does **not** write the manifest (assert file absent after `LoadFilesAsync`),
- enable/disable round-trip preserving a nested relative path,
- the conflict row (same path seeded under both roots).

`ModsFolderUseCaseTests` needs its Moq setups retyped; the assertions stand.

---

## Phase 2 — Folder view

Group rows by `Path.GetDirectoryName(RelativePath)` in the view model behind a "Group by
folder" toggle; flat list stays the default. Avalonia's built-in `TreeView` covers it —
no dependency.

Also surface the `.ts4script` depth warning here: a script deeper than one level below
the Mods root will not load. Cheap, and users hit it constantly.

---

## Phase 3 — Install pipeline + records

### Install record

```
InstallRecord(InstallId, Source{Provider, ModPageUrl, DownloadUrl}, Version?,
              InstalledUtc, SourceArchivePath?,
              Files[{RelativePath, Sha256, SizeBytes}],
              SkippedEntries[])
```

### `ArchiveInstallService` (new, Infrastructure)

Replaces `WickedWhimsArchiveInstaller` (delete it — its path-traversal guard is worth
keeping and carrying over). Two methods:

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

**`Install(archivePath, selection, layout, modFolderName)`**
- Extracts selected entries under `Mods/<modFolderName>/` — one folder per mod, which is
  what makes phase 2's folder view meaningful for anything we install.
- Any `.ts4script` that would land deeper than one level is **flattened to the mod folder
  root**, since the game won't load it otherwise. Record the remapped path.
- Hashes each written file; returns an `InstallRecord`.
- Bare `.package` / `.ts4script` downloads (no archive) install as a single file.
- Non-zip archives (`.rar`, `.7z`) fail with "extract manually, then use Install from
  file" — `System.IO.Compression` is zip-only. Mark with a `ponytail:` comment naming
  SharpCompress as the upgrade path.

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

- `BrowserTabViewModel.DownloadAsync`: after `MarkCompleted`, offer "Install to Mods" →
  preview → install.
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
- New infrastructure tests against a temp sandbox: reads don't write; conflicted path;
  nested-path enable/disable round trip; archive install writes only selected entries and
  flattens a deep `.ts4script`; update deletes stale paths from the prior record.
- Manual: run `ModManager.Ui`, point it at a scratch Mods folder with nested subfolders
  and a deliberately mixed archive (packages + readme + an `Optional/` variant), confirm
  the preview classifies correctly and the folder view groups as expected.
- CLI regression: `dotnet run --project ModManager/ModManager.Cli -- --check` must still
  work unchanged after phase 1.
