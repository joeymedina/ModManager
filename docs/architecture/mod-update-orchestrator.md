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

`WickedWhimsUpdateStrategy` coordinates four focused infrastructure helpers:

```text
WickedWhimsUpdateStrategy
 ├─ WickedWhimsVersionDetector   – scans mods folder for installed version
 ├─ WickedWhimsReleaseClient     – fetches latest release metadata from official site
 │                                  and downloads the archive when requested
 └─ WickedWhimsArchiveInstaller  – extracts the archive safely into the mods folder
```

### Version Detection

- Scans all `.ts4script`, `.package`, and `.py` files under the mods folder.
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

### Archive Installation

- Extracts the ZIP archive byte-for-byte into the target mods folder.
- Each entry destination is validated to remain under the target root (path-traversal guard).
- Existing files are overwritten; new directories are created on demand.
- Returns the count of written files.

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
| `WickedWhimsArchiveInstaller` | — | Singleton |

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
