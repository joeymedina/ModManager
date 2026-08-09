# ModsFolderService Architecture Decisions

## Context
`ModsFolderService` is the filesystem-backed repository used by the application use case
for flat mod-file discovery and file-level state management (enable/disable/delete) for
Sims 4 mod files. It replaced an earlier design that grouped files into `ManagedMod`
"packages" by filename-prefix guessing; see
[flat-mod-listing-install-records.md](../flat-mod-listing-install-records.md) and
[mod-listing-and-update-tracking.md](./mod-listing-and-update-tracking.md) for why.

## Layering / Clean Architecture

- `IModsFolderRepository` and `IModsFolderUseCase` are defined in the **Application** layer.
- `ModsFolderService` is in **Infrastructure** and implements `IModsFolderRepository`.
- The `ModFile` / `ModFileFailure` models live in **Application/Models**.
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

There is currently no manifest service — see "Manifest" below.

## Folder State Strategy

- Active mod files live under the configured `Mods` folder.
- Disabled mod files live in a sibling folder named `Mods.Disabled`.
- Enabling/disabling operates on individual files (by relative path) as moves between these two roots.

## Manifest

Phase 1 has no manifest. `ModsManifestService` and the old `%APPDATA%/ModManager/mods-manifest.json`
were deleted outright — nothing in phase 1 writes user metadata (display names, groups,
install links), so there is nothing to persist yet. The old AppData file, if present, is
left on disk untouched and unread.

A per-folder manifest (`Path.Combine(layout.ModsFolderPath, ".modmanager.json")`) is
planned for phase 3, once install records are the first thing that needs it. See
[flat-mod-listing-install-records.md](../flat-mod-listing-install-records.md).

## File Identity

- **`RelativePath`** (normalized, `/`-separated) is the identity of a `ModFile`. There is
  no separate stable ID — the relative path already survives enable/disable moves because
  `ModsFileOperationsService` preserves it when moving between roots.
- Renaming a file outside the manager breaks any link to metadata keyed on the old path.
  Accepted trade-off; see the architecture direction doc for the mitigation sketch
  (opportunistic hashing), not implemented.

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

## Dependency Injection

- `IModsFolderUseCase` -> `ModsFolderUseCase` (Application DI).
- `IModsFolderRepository` -> `ModsFolderService` (Infrastructure DI).
- `ModsFolderPathService`, `ModsDiscoveryService`, and `ModsFileOperationsService` are registered in Infrastructure DI.

## Operational Flow

- `LoadFilesAsync`
  1. Resolve folder paths (no directory creation).
  2. Discover files from `Mods` and `Mods.Disabled` as a flat list.
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
  4. Return the aggregated list of failures.

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

## Test Strategy

- **Application tests** (`ModsFolderUseCaseTests`) use Moq for repository behavior/contract verification.
- **Infrastructure tests** (`ModsFolderServiceTests`) are filesystem integration-style tests against a temp sandbox, covering: a read writes nothing and creates no `Mods.Disabled`; a nested-path enable/disable round trip; the conflict row; and partial-failure reporting on a bulk operation.
- Keep the split: mocking at use-case boundary, real IO assertions at infrastructure boundary.

## Future Improvements (Planned)

- Phase 2: folder-tree view over the flat list; `.ts4script` depth warning.
- Phase 3: reintroduce a per-folder manifest; install pipeline and install records; version-aware, record-driven updates.
- Phase 4: adoption flow linking pre-existing files to a source.
- Phase 5: manual groups.
