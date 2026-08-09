# ModsFolderService Architecture Decisions

## Context
`ModsFolderService` is the filesystem-backed repository used by the application use case for mod discovery and mod state management (enable/disable/delete) for Sims 4 mods.

## Layering / Clean Architecture

- `IModsFolderRepository` and `IModsFolderUseCase` are defined in the **Application** layer.
- `ModsFolderService` is in **Infrastructure** and implements `IModsFolderRepository`.
- Mod workflow models are stored in **Application/Models** (including `Models/Mods/*`).
- Infrastructure contains IO behavior and adapters, while orchestration contracts stay in Application.
- Team standard: one class per file; no nested model classes inside services.

## Service Decomposition

`ModsFolderService` is intentionally thin and delegates to focused helper services. It exposes two constructors:

- **Parameterized constructor** — used by DI; receives all four helpers as injected singletons.
- **Parameterless constructor** — used by infrastructure tests; constructs helpers directly with `new` so tests can instantiate `ModsFolderService` without a service provider.

Helper services:

- `ModsFolderPathService`
  - Resolves `Mods` and sibling `Mods.Disabled` paths.
  - Validates relative paths resolve under expected roots.
- `ModsManifestService`
  - Loads/saves `%APPDATA%/ModManager/mods-manifest.json`.
  - Gets/creates profile entries keyed by absolute mods-folder path.
- `ModsDiscoveryService`
  - Discovers supported mod files from active/disabled roots.
  - Groups discovered files into logical mods.
  - Maps `ManagedMod` to manifest records.
- `ModsFileOperationsService`
  - Performs enable/disable move operations.
  - Performs delete operations.
  - Cleans up empty directories after file operations.

## Folder State Strategy

- Active mods live under the configured `Mods` folder.
- Disabled mods live in a sibling folder named `Mods.Disabled`.
- Enabling/disabling a mod is implemented as moving its files between these two roots.

## Internal Coordination Type

`RepositoryState` (Application/Models/Mods) is a private-by-convention record returned by the internal `LoadStateAsync` helper. It bundles everything needed for a single operation:

```text
RepositoryState
  Layout   : ModsFolderLayout    – resolved folder paths
  Manifest : ManifestModel        – full JSON manifest loaded from AppData
  Profile  : ManifestProfile      – the profile for the current mods folder
  Mods     : IReadOnlyList<ManagedMod> – discovered mods for this call
```

Grouping these into one record keeps every operation atomic with respect to state loading and avoids passing four arguments between private methods.

## Mod Identity and Persistence

- Mod identity is stable and represented by `ModId`.
- IDs and related metadata are persisted in a JSON manifest at:
  - `%APPDATA%/ModManager/mods-manifest.json`
- Manifest supports multiple profiles keyed by the absolute `Mods` folder path.

## Discovery and Grouping

- Mod files are discovered from both `Mods` and `Mods.Disabled`.
- Supported file extensions: `.package`, `.ts4script`.
- Current grouping heuristic: **prefix before first `_` or `-`** from file name (without extension).
- This grouping heuristic is temporary and should be revisited in a later iteration.

## Safety and Consistency Rules

- All computed file paths are validated to remain under expected roots.
- Move operations fail on destination conflicts (no silent overwrite).
- Empty directories created by file moves/deletes are cleaned up.
- Delete removes all files associated with a mod and then updates manifest state.

## Dependency Injection

- `IModsFolderUseCase` -> `ModsFolderUseCase` (Application DI).
- `IModsFolderRepository` -> `ModsFolderService` (Infrastructure DI).
- `ModsFolderPathService`, `ModsManifestService`, `ModsDiscoveryService`, and `ModsFileOperationsService` are registered in Infrastructure DI.

## Operational Flow

- `LoadModsAsync`
  1. Resolve folder paths and ensure directories exist.
  2. Load manifest and get/create profile for the mods root.
  3. Discover files from `Mods` and `Mods.Disabled`, then group into logical mods.
  4. Persist merged profile state back to manifest.
- `EnableModAsync` / `DisableModAsync`
  1. Load current state.
  2. Resolve target mod by `ModId`.
  3. Move files between active/disabled roots.
  4. Call `LoadModsAsync` to rediscover and re-persist state (this is a second manifest save; the first save occurs inside `LoadModsAsync` when it snapshots the newly discovered mod list).
  5. Return the refreshed `ManagedMod` for the changed mod ID.
- `DeleteModAsync`
  1. Load current state.
  2. Resolve target mod by `ModId`.
  3. Delete all associated files.
  4. Remove mod from manifest profile and persist.

## Naming Semantics

- `ModId`: stable persisted identifier used for commands and UI actions.
- `PackageKey`: technical grouping key derived from file name prefix heuristic.
- `Name`: display-friendly name (currently defaults to `PackageKey` unless overridden via manifest).

## Error and Conflict Behavior

- Invalid or unsafe relative paths throw `InvalidOperationException`.
- Missing requested mod ID throws `InvalidOperationException`.
- Enable/disable fails when destination file already exists (no overwrite).
- Missing source file during move throws `FileNotFoundException`.

## Test Strategy

- **Application tests** (`ModsFolderUseCaseTests`) use Moq for repository behavior/contract verification.
- **Infrastructure tests** (`ModsFolderServiceTests`) are filesystem integration-style tests validating real file moves/deletes and manifest-stable IDs.
- Keep the split: mocking at use-case boundary, real IO assertions at infrastructure boundary.

## Future Improvements (Planned)

- Replace filename-prefix grouping with a more explicit package model.
- Add configurable conflict policies for move operations.
- Expand manifest metadata (hashes, source URL, version tracking).
- Add repair/reconciliation flow for manifest drift vs filesystem state.
