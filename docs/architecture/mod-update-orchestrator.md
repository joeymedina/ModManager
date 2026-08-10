# Mod Update Orchestrator Architecture

## Context

`ModUpdateOrchestrator` is the application-layer entry point for checking and downloading mod updates. It dispatches to a registered, mod-specific `IModUpdateStrategy` and returns a unified `ModUpdateResult`. The CLI consumes it directly; the UI update page will consume it in a future iteration.

## Layering / Clean Architecture

- `IModUpdateOrchestrator` and `IModUpdateStrategy` are defined in the **Application** layer.
- `ModUpdateOrchestrator` is in the **Application** layer (pure orchestration, no IO).
- `WickedWhimsUpdateStrategy` and its helpers are in **Infrastructure**.
- No infrastructure type is referenced from the Application layer.

## Interfaces

```text
IModUpdateOrchestrator
  ExecuteAsync(ModUpdateRequest, CancellationToken) -> Task<ModUpdateResult>

IModUpdateStrategy
  ModId                                             -> string
  ExecuteAsync(ModUpdateRequest, CancellationToken) -> Task<ModUpdateResult>
```

`ModUpdateOrchestrator` receives all registered `IModUpdateStrategy` instances via constructor injection, indexes them by `ModId` (case-insensitive), and delegates `ExecuteAsync` to the matching strategy.

## Models

| Type | Layer | Description |
| --- | --- | --- |
| `ModUpdateRequest` | Application | Carries `ModId`, `ModsFolder`, and `DownloadIfUpdateAvailable` flag |
| `ModUpdateResult` | Application | Carries installed version, latest release info, comparison result, and download metadata |
| `ModVersionInfo` | Application | Installed version string and source file path |
| `ModReleaseInfo` | Application | Latest version string and optional release date |

## WickedWhims Strategy Pipeline

`WickedWhimsUpdateStrategy` coordinates three focused infrastructure helpers plus its own
`ModsManifestService` dependency for install-record tracking:

```text
WickedWhimsUpdateStrategy
 ├─ ModsFolderPathService     – resolves the enabled/disabled root pair
 ├─ ModsManifestService       – loads/saves the shared per-folder manifest
 ├─ WickedWhimsVersionDetector – scans (or scans-scoped-to-a-record) for installed version
 └─ WickedWhimsReleaseClient  – fetches latest release metadata from official site
                                  and downloads the archive when requested
```

There is no separate `WickedWhimsArchiveInstaller` anymore — it was deleted once the
general-purpose `ArchiveInstallService` shipped (see
[mods-folder-service.md](./mods-folder-service.md)), but WickedWhims still needs its own
flat, path-traversal-guarded extraction (`ExtractArchive`, a private method on the
strategy) rather than routing through that service: `ArchiveInstallService.Install` always
mints a fresh, deduped `Mods/<name>/` folder, which is right for a first-time install but
wrong for an *update* — an update needs to land in the mod's existing folder (or root), not
a new one. So the extraction logic stayed small and inlined rather than being forced
through a service shaped for a different case.

### Install-record tracking (the three bugs this fixes)

`WickedWhimsUpdateStrategy` now writes and reads its own `InstallRecord` in the shared
manifest, keyed by `InstallSource.Provider == "wickedwhims"`:

1. **Stale files.** On a successful update, any path present in the *previous* record but
   absent from the newly-extracted file set is deleted. Previously, version-stamped
   filenames (`WickedWhims_v187a.package` → `v188b.package`) left both versions installed
   and the game loaded both.
2. **Disabled-root awareness.** Before extracting, the strategy checks whether the
   previous record's files currently live under the disabled root (`Mods.Disabled`) rather
   than assuming the enabled `Mods` root, and targets whichever one they're actually in.
   Updating a disabled mod no longer silently re-enables it.
3. **Scoped version scanning.** `WickedWhimsVersionDetector.FindInstalledVersion` takes an
   optional `scopedRelativePaths` collection; when a previous record exists, the scan is
   limited to just its files instead of reading every `.package`/`.py`/`.ts4script` in the
   tree with `File.ReadAllBytes` — a non-starter on a 40 GB folder. A full-tree scan only
   happens when there's no record yet (first-time detection).

The record itself: `Source.ModPageUrl` is the itch.io page (`WickedWhimsReleaseClient.ItchPage`,
now `internal` instead of `private const` so the strategy can reference it), and
`Source.DownloadUrl` is the actual resolved, per-request-signed file URL —
`WickedWhimsReleaseClient.DownloadLatestArchiveAsync` returns a
`WickedWhimsDownload(Url, Bytes)` record instead of bare `byte[]` so that URL is available
to record. `SourceArchivePath` stays `null`: the download only ever exists as an in-memory
byte array, never a file on disk.

### Version Detection

- Scans all `.ts4script`, `.package`, and `.py` files under the mods folder (or just the
  paths named by a prior `InstallRecord`, once one exists — see above).
- For `.ts4script` files: opens them as ZIP archives and inspects `__init__.py` entries for `__version__` / `VERSION` constants.
- Falls back to text pattern matching against `WickedWhims vXXXa` / `TURBODRIVER ... vXXXa` patterns.
- Selects the highest found version; returns `null` when none is found.
- Version strings use the format `NNNa` (e.g., `187a`, `188b`): a numeric component followed by an optional letter suffix.

### Version Comparison

`WickedWhimsVersionDetector.CompareVersions` uses a two-level comparison:

1. Compare the numeric component as integers.
2. On tie, compare the letter suffix lexicographically.

Returns `null` when either string cannot be parsed.

### Release Fetching

- Fetches `https://wickedwhimsmod.com/download/` and parses version and release date from HTML.
- Downloads archives via `https://turbodriver.itch.io/wickedwhims` using the itch.io CSRF token / upload ID flow.
- `HttpClient` is configured with a `ModManager/1.0` user agent.
- Returns `WickedWhimsDownload(Url, Bytes)` — see "Install-record tracking" above for why
  the URL travels with the bytes now.

### Archive Installation

- `WickedWhimsUpdateStrategy.ExtractArchive` (private, inline) extracts the ZIP archive
  byte-for-byte into the target root (enabled or disabled — see above), flat, since
  WickedWhims ships a flat file set rather than a subfoldered mod.
- Each entry destination is validated to remain under the target root (path-traversal guard,
  carried over from the deleted `WickedWhimsArchiveInstaller`).
- Existing files are overwritten; new directories are created on demand.
- Each written file is hashed (`FileHashing.ComputeSha256`, shared with
  `ArchiveInstallService` and `ModsFolderService.AdoptAsync`) to build the new
  `InstallRecord`'s file list.

## CLI Wiring

`ModManager.Cli` hosts the full service graph via `Microsoft.Extensions.Hosting` and calls `IModUpdateOrchestrator.ExecuteAsync` with options parsed by `CliOptions`.

```text
CLI args
  --check | --download  (default: --check)
  --mod <id>            (default: wickedwhims)
  --folder <path>       (default: ~/Documents/Mods)

Exit codes
  0   – success
  1   – error
  130 – canceled
```

## Dependency Injection

Registered via `AddApplicationServices` and `AddInfrastructureServices`:

| Registration | Implementation | Lifetime |
| --- | --- | --- |
| `IModUpdateOrchestrator` | `ModUpdateOrchestrator` | Singleton |
| `IModUpdateStrategy` | `WickedWhimsUpdateStrategy` | Singleton |
| `WickedWhimsVersionDetector` | — | Singleton |
| `WickedWhimsReleaseClient` | — | Singleton |
| `ModsFolderPathService` | — | Singleton (shared with the mods-folder feature) |
| `ModsManifestService` | — | Singleton (shared with the mods-folder feature) |

`WickedWhimsArchiveInstaller` is gone — deleted once its extraction logic moved inline
into `WickedWhimsUpdateStrategy` (see "WickedWhims Strategy Pipeline" above).

`IEnumerable<IModUpdateStrategy>` is resolved by the DI container and passed to `ModUpdateOrchestrator`. Adding a new strategy requires registering a new `IModUpdateStrategy` implementation in `AddInfrastructureServices`.

## Error Behavior

| Condition | Exception |
| --- | --- |
| `request` is null | `ArgumentNullException` |
| `request.ModId` is blank | `ArgumentException` |
| No strategy registered for `ModId` | `InvalidOperationException` |
| No WickedWhims version found in mods folder | `InvalidOperationException` |
| Official page does not contain version | `InvalidOperationException` |
| Archive contains an unsafe path | `InvalidOperationException` |

## Test Strategy

- **Application tests** (`ModUpdateOrchestratorTests`) cover null-guard, blank-ID, missing-strategy, and happy-path dispatch using `StubModUpdateStrategy` (no mocks).
- Infrastructure strategy tests are deferred until HTTP interactions are abstracted behind an interface.

## Future Improvements (Planned)

- Abstract `HttpClient` calls behind a testable interface for WickedWhims release/download steps.
- Add support for additional mods by registering new `IModUpdateStrategy` implementations.
- Surface update results in the UI updates page.
- Add retry/back-off for transient network failures.
