# ModsFolderService Architecture Decisions

## Context
`ModsFolderService` is the filesystem-backed repository used by the application use case
for flat mod-file discovery, file-level state management (enable/disable/delete), the
install pipeline, adoption of pre-existing files, and manual groups for Sims 4 mod files.
It replaced an earlier design that grouped files into `ManagedMod` "packages" by
filename-prefix guessing; see
[flat-mod-listing-install-records.md](../flat-mod-listing-install-records.md) and
[mod-listing-and-update-tracking.md](./mod-listing-and-update-tracking.md) for why. This
document covers the final shape after all five phases of that rework.

## Layering / Clean Architecture

- `IModsFolderRepository` and `IModsFolderUseCase` are defined in the **Application** layer.
- `ModsFolderService` is in **Infrastructure** and implements `IModsFolderRepository`.
- The `ModFile`, `ModFileFailure`, `ModsManifest`, `InstallRecord`, and `ModGroup` models
  live in **Application/Models**.
- Infrastructure contains IO behavior and adapters, while orchestration contracts stay in Application.
- Team standard: one class per file; no nested model classes inside services.

## Service Decomposition

`ModsFolderService` is intentionally thin and delegates to focused helper services. It exposes two constructors:

- **Parameterized constructor** — used by DI; receives all injected singletons.
- **Parameterless constructor** — used by infrastructure tests; constructs helpers directly with `new` so tests can instantiate `ModsFolderService` without a service provider.

Helper services:

- `ModsFolderPathService`
  - Resolves `Mods` and sibling `Mods.Disabled` paths.
  - Validates relative paths resolve under expected roots.
- `ModsDiscoveryService`
  - Discovers supported mod files from active/disabled roots into a flat, sorted list.
  - Pure: no manifest argument, no writes, no directory creation.
- `ModsFileOperationsService`
  - Performs bulk enable/disable move operations and bulk delete operations.
  - Continues past per-file failures and returns them rather than throwing.
  - Cleans up empty directories after file operations.
- `ModsManifestService`
  - Loads/saves the per-folder manifest (see "Manifest" below). Load never writes.
- `FileHashing` (internal static helper)
  - `ComputeSha256(path)` — shared by `ArchiveInstallService`, `WickedWhimsUpdateStrategy`,
    and `ModsFolderService.AdoptAsync`. Extracted once a third call site needed the same
    open-file-and-hash logic.

`ArchiveInstallService` (behind `IArchiveInstallService`) is a sibling service, not a
`ModsFolderService` helper — see "Install pipeline" below.

## Folder State Strategy

- Active mod files live under the configured `Mods` folder.
- Disabled mod files live in a sibling folder named `Mods.Disabled`.
- Enabling/disabling operates on individual files (by relative path) as moves between these two roots.

## Manifest

A per-folder manifest lives at `Path.Combine(layout.ModsFolderPath, ".modmanager.json")`
(inside the `Mods` folder itself, so it travels with the folder — not `%APPDATA%`). The old
AppData manifest, if present, is left on disk untouched and unread; nothing in it was worth
migrating. `.json` is not a supported mod extension, so discovery ignores the file and so
does the game.

```
ModsManifest(SchemaVersion, Files[], Groups[], Installs[])
  ManifestFileEntry(RelativePath, DisplayName?, GroupId?, Notes?)   // sparse
  ModGroup(GroupId, Name, Members[])                                 // relative paths
  InstallRecord(InstallId, Source, Version?, InstalledUtc,
                SourceArchivePath?, Files[], SkippedEntries[])
    InstallSource(Provider, ModPageUrl?, DownloadUrl?)
    InstallRecordFile(RelativePath, Sha256, SizeBytes)
```

- **Sparse**: a file with no user metadata gets no `ManifestFileEntry` row. A fresh folder
  yields an empty manifest (`ModsManifest.Empty`).
- **`LoadAsync`** returns `ModsManifest.Empty` for a missing file, unreadable JSON, or a
  `SchemaVersion` older than `ModsManifest.CurrentSchemaVersion` — never throws.
- **Reads never write.** Nothing calls `SaveAsync` from a read path.
- **First writers**: `ArchiveInstallService.InstallAsync`, `ModsFolderService.AdoptAsync`,
  `ModsFolderService.AddToGroupAsync`/`RemoveFromGroupAsync`, and
  `WickedWhimsUpdateStrategy` (via its own `ModsManifestService` dependency) all write to
  the same manifest shape.
- **`Provider` values in the wild**: `"manual"` (Install-from-file panel with no browser
  context), `"browser"` (an install triggered from a completed download, which also
  populates `ModPageUrl`/`DownloadUrl` when known), `"adopted"` (the adoption flow), and
  `"wickedwhims"` (the CLI's automated update strategy, keyed by this value to find its own
  prior `InstallRecord`).

## Install pipeline (`ArchiveInstallService` / `IArchiveInstallService`)

A sibling Infrastructure service — not archive-shaped work belongs on
`IModsFolderRepository` (adoption and groups don't touch an archive, so they live there
instead; see below). Two methods, both returning a result type instead of throwing:

```
ArchiveInstallResult<T>(bool Success, T? Value, string? Error)
```

- **`PreviewAsync(archivePath)`** classifies zip entries without writing anything:
  - `.package`/`.ts4script` → `Installable`, selected by default.
  - Entries under a folder matching `optional|alternate|extras?`, or `.package` files that
    share a stem (the text before the first `_`/`-`) with a sibling in the same directory →
    `Variant`, deselected by default — an archive with an `Optional/` folder or multiple
    same-slot skins can't be resolved automatically, so the preview makes it a one-click
    decision.
  - Everything else → `NotInstallable`.
  - A bare `.package`/`.ts4script` (no archive) previews as a single installable entry.
  - A non-`.zip` extension (`.rar`, `.7z`, ...) fails with a message pointing at manual
    extraction — `System.IO.Compression` is zip-only.
- **`InstallAsync(archivePath, selectedEntryNames, layout, displayName, source, version)`**
  - Sanitizes `displayName` into a folder name, de-duping against a collision with a
    numeric suffix (`Foo`, `Foo (2)`, ...) — never a silent overwrite.
  - Extracts selected entries under `Mods/<name>/`, carrying over the path-traversal guard
    from the deleted `WickedWhimsArchiveInstaller`.
  - Flattens any `.ts4script` landing more than one level deep to the mod folder root
    (matches the phase-2 depth-warning rule: a script only loads at depth ≤ 1).
  - Hashes every written file (`FileHashing.ComputeSha256`) and returns an `InstallRecord`.
  - Persists it: removes any existing `ManifestFileEntry` for the written paths, adds fresh
    ones with `DisplayName`, appends the `InstallRecord` to `Installs`.

## File Identity and Manifest Merge

- **`RelativePath`** (normalized, `/`-separated) is the identity of a `ModFile`. There is
  no separate stable ID — the relative path already survives enable/disable moves because
  `ModsFileOperationsService` preserves it when moving between roots.
- `LoadFilesAsync` layers manifest data onto each discovered `ModFile` by relative path:
  - `DisplayName` / `GroupId` — from the matching `ManifestFileEntry`, if any.
  - `InstallId` / `Version` / `InstalledUtc` / `Provider` — from whichever `InstallRecord`
    lists this path in its `Files[]` (last-appended record wins per path, which is why
    re-adopting or re-installing over the same paths doesn't need special-case pruning of
    the old record).
- Renaming a file outside the manager breaks any link to metadata keyed on the old path.
  Accepted trade-off; see the architecture direction doc for the mitigation sketch
  (opportunistic hashing), not implemented.

## Adoption (`ModsFolderService.AdoptAsync`)

Links already-discovered files to a source **without moving anything** — metadata only.
Lives on `IModsFolderRepository`/`IModsFolderUseCase` rather than
`IArchiveInstallService` because it never touches an archive; it fits the existing
bulk path-based convention (`EnableAsync`/`DisableAsync`/`DeleteAsync`) instead.

- Validates every selected path resolves under either root first — all-or-nothing, fails
  with a message naming the missing ones rather than adopting a partial set.
- Hashes each file where it already sits, builds an `InstallRecord` with
  `Source.Provider = "adopted"`, `SourceArchivePath = null` (there's no archive), and the
  user-supplied `DisplayName` (required) / `Version` / `ModPageUrl` (both optional — often
  not knowable for something being retroactively tagged).
- Re-adopting an already-tracked file is allowed, not blocked; see the last-write-wins note
  above.

## Manual groups (`LoadGroupsAsync` / `AddToGroupAsync` / `RemoveFromGroupAsync`)

Purely cosmetic and virtual, per the original direction doc: nothing in discovery,
file-ops, or update logic reads groups, and a file belongs to at most one group
(`ManifestFileEntry.GroupId` is a single nullable field, so this is enforced by the model
shape, not extra validation).

- **`LoadGroupsAsync`** returns the raw `ModGroup` list — including members whose path no
  longer resolves to a discovered file. This is deliberate: `LoadFilesAsync`'s discovered
  list has no way to represent a file that isn't there, so the UI needs the manifest's
  membership list directly to render a stale member as "missing" instead of silently
  dropping it (a rename or external delete shouldn't erase the user's intent).
- **`AddToGroupAsync(modsFolderPath, relativePaths, groupName)`** reuses an existing group
  by case-insensitive name match, or mints one. Each added path is removed from whatever
  group it previously belonged to (enforcing single-membership) and that prior group is
  dropped if left with zero members. All-or-nothing on missing paths, like `AdoptAsync`.
- **`RemoveFromGroupAsync(modsFolderPath, relativePaths)`** clears `GroupId` on the given
  paths, drops a group left empty, and drops a `ManifestFileEntry` left with no
  `DisplayName`/`GroupId`/`Notes` at all — matching the sparse-manifest principle.

## Discovery

- Mod files are discovered from both `Mods` and `Mods.Disabled` via
  `DirectoryInfo.EnumerateFiles(..., AllDirectories)`, which yields `SizeBytes` and
  `ModifiedUtc` from the directory walk itself.
- Supported file extensions: `.package`, `.ts4script`.
- No grouping. `DiscoverFiles(layout)` returns one row per file, sorted by `RelativePath`.
- **Conflict rule**: a relative path present under both roots yields a single row with
  `IsConflicted = true` and `State = Enabled`. Enable/disable on a conflicted row fails
  with a per-file reason; delete removes both copies. This is reachable today because
  `MoveFilesForStateChangeAsync` refuses to overwrite an occupied destination and leaves
  the source behind.

## Safety and Consistency Rules

- All computed file paths are validated to remain under expected roots.
- Move operations skip (rather than throw on) destination conflicts, recording a failure.
- Empty directories created by file moves/deletes are cleaned up.
- **Reads create nothing.** `LoadFilesAsync` does not call `Directory.CreateDirectory` on
  either root; a `Mods.Disabled` folder only appears once something is actually disabled
  into it.
- Archive extraction (`ArchiveInstallService`, and `WickedWhimsUpdateStrategy`'s own inline
  extractor) validates every entry's resolved path stays under the target root before
  writing — guards against zip-slip path traversal.

## Dependency Injection

- `IModsFolderUseCase` -> `ModsFolderUseCase` (Application DI).
- `IModsFolderRepository` -> `ModsFolderService` (Infrastructure DI).
- `IArchiveInstallService` -> `ArchiveInstallService` (Infrastructure DI).
- `ModsFolderPathService`, `ModsDiscoveryService`, `ModsFileOperationsService`, and
  `ModsManifestService` are registered in Infrastructure DI as concrete singletons.

## Operational Flow

- `LoadFilesAsync`
  1. Resolve folder paths (no directory creation).
  2. Discover files from `Mods` and `Mods.Disabled` as a flat list.
  3. Load the manifest (never writes) and merge `DisplayName`/`GroupId`/`InstallId`/
     `Version`/`InstalledUtc`/`Provider` onto each file. Skipped entirely (files returned
     as-is) when the manifest has no `Files` or `Installs` rows.
- `EnableAsync` / `DisableAsync`
  1. Discover the current flat file list.
  2. Match each requested relative path against it; unmatched paths become failures.
  3. Conflicted matches become failures (must be resolved before a state change).
  4. Move the remaining files between active/disabled roots, continuing past per-file
     failures (locked file, occupied destination).
  5. Return the aggregated list of failures (empty means everything succeeded).
- `DeleteAsync`
  1. Discover the current flat file list.
  2. Match requested paths; unmatched paths become failures.
  3. Delete each matched file from whichever root(s) it exists in (both, for a conflicted
     path), continuing past per-file failures.
  4. Return the aggregated list of failures. (Manifest rows referencing a deleted path are
     **not** pruned today — a known gap, see below.)
- `AdoptAsync` / `AddToGroupAsync` / `RemoveFromGroupAsync` — see their sections above.

## Error and Conflict Behavior

- Invalid or unsafe relative paths throw `InvalidOperationException` (unchanged from before).
- A requested path that does not exist becomes a `ModFileFailure("File not found.")`
  rather than an exception — the caller may have selected a stale row.
- Enable/disable on a conflicted path becomes a `ModFileFailure` explaining the conflict.
- A locked file or occupied destination during a move/delete becomes a `ModFileFailure`
  with the underlying `IOException`/`UnauthorizedAccessException` message; the rest of
  the batch still proceeds.
- **No rollback.** A partially applied batch is not undone — undoing file moves is itself
  a risky write, and failed paths can simply be retried.
- `AdoptAsync`/`AddToGroupAsync` are all-or-nothing instead: a missing path fails the whole
  call with a message naming it, rather than partially adopting/grouping.

## Test Strategy

- **Application tests** (`ModsFolderUseCaseTests`) use Moq for repository behavior/contract verification.
- **Infrastructure tests** (`ModsFolderServiceTests`) are filesystem integration-style tests against a temp sandbox, covering: a read writes nothing and creates no `Mods.Disabled`; a nested-path enable/disable round trip; the conflict row; partial-failure reporting on a bulk operation; adoption writing without moving files and failing cleanly on a missing path; group creation/reuse-by-name, moving a file between groups, auto-pruning an emptied group, and a missing member still surfacing from `LoadGroupsAsync`.
- **`ArchiveInstallServiceTests`** cover preview classification (installable/variant/not-a-mod-file), non-zip rejection, selective extraction, `.ts4script` flattening, folder-name dedup, the manifest round-trip through `LoadFilesAsync`, and the bare-file install path.
- Keep the split: mocking at use-case boundary, real IO assertions at infrastructure boundary.

## Known Gaps / Deferred

- **Non-zip archives** (`.rar`, `.7z`) aren't supported — `System.IO.Compression` is
  zip-only. Marked with a `ponytail:` comment in `ArchiveInstallService` naming
  SharpCompress as the upgrade path if this turns out to matter.
- **Delete doesn't prune manifest rows.** Deleting a tracked file's underlying `ModFile`
  leaves its `ManifestFileEntry`/`InstallRecord` reference dangling in the manifest. Not
  currently a correctness problem (nothing reads a manifest row for a path that no longer
  discovers), but worth cleaning up if the manifest ever needs to stay minimal.
- **Opportunistic rehashing for renamed files** (sketched in the direction doc) was never
  built — a rename outside the app still orphans metadata.
