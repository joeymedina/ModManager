# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

A Sims 4 mod manager: an Avalonia desktop app plus a CLI, over a shared clean-architecture core.
.NET 10, C#, solution file is `ModManager.slnx` (XML solution format, not `.sln`).

## Commands

```bash
dotnet build ModManager.slnx
dotnet test ModManager.slnx
dotnet run --project ModManager.Ui
dotnet run --project ModManager.Cli -- --check --mod wickedwhims --folder "C:\Users\<you>\Documents\Electronic Arts\The Sims 4\Mods"
```

Single test or test class:

```bash
dotnet test ModManager.slnx --filter "FullyQualifiedName~ModsFolderServiceTests"
```

```bash
dotnet test ModManager.slnx --filter "Name~InstallAsync_WhenFolderNameCollides"
```

- **Build fails with a locked output folder** when the app is running. Build elsewhere with
  `-p:ArtifactsPath=<dir>` rather than killing the app.
- Tests reference `ModManager.Ui` (a `WinExe` with a WebView2 native reference), so the suite is
  effectively Windows-only. CI runs `windows-latest`.
- UI logs go to `%LOCALAPPDATA%\ModManager\logs\app-.log` (daily rolling, 14 kept). Set
  `MODMANAGER_LOG_LEVEL=Debug` to get per-file extraction and hash logging without a rebuild.
- CLI exit codes: `0` success, `1` error, `130` canceled.

## Architecture

Dependencies point inward. `Ui` and `Cli` are both composition roots over the same core:

```
ModManager.Ui (Avalonia)  ─┐
ModManager.Cli (Host)     ─┴─> ModManager.Application ──> (implemented by) ModManager.Infrastructure
```

- **Application** owns the interfaces *and* the domain models (`ModFile`, `ModsManifest`,
  `InstallRecord`, `ModGroup`, `ModUpdate*`). It references no infrastructure type.
- **Infrastructure** is implementation detail only — filesystem IO, archive extraction, HTTP,
  persistence DTOs. Do not put domain models here.
- **Ui** reaches the filesystem *only* through `IModsFolderUseCase` and `IArchiveInstallService`.
  A view model touching `System.IO` directly is a layering break.

### Two feature spines

1. **Mods folder** — `IModsFolderUseCase` → `IModsFolderRepository` → `ModsFolderService`, which is
   deliberately thin and delegates to `ModsFolderPathService` / `ModsDiscoveryService` /
   `ModsFileOperationsService` / `ModsManifestService`. `ArchiveInstallService` is a sibling, not a
   helper.
2. **Updates** — `IModUpdateOrchestrator` dispatches by `ModId` to a registered `IModUpdateStrategy`.
   Adding a mod means registering a new strategy in `AddInfrastructureServices`; nothing else changes.

### Two invariants worth knowing before editing anything

- **Enabled/disabled is a file move between two sibling roots**: `Mods/` and `Mods.Disabled/`. A path
  present under *both* is a `IsConflicted` row that refuses state changes.
- **`RelativePath` (normalized, `/`-separated) is a file's identity.** All metadata —
  display name, group, install record — lives in `.modmanager.json` *inside the Mods folder* and is
  keyed on that path. Reads never write it; `LoadAsync` returns `ModsManifest.Empty` rather than
  throwing. Renaming a file outside the app orphans its metadata (known, accepted).

### Dependency injection

Three extension methods — `AddApplicationServices`, `AddInfrastructureServices`, `AddUiServices` —
composed in [App.axaml.cs](ModManager.Ui/App.axaml.cs) (UI) and [Program.cs](ModManager.Cli/Program.cs) (CLI).
Services take an optional logger via primary constructor
(`ILogger<T>? logger = null` falling back to `NullLogger<T>.Instance`), so an unresolvable constructor
would otherwise only surface as a crash on launch — `ModManager.Tests/Ui/ServiceRegistrationTests.cs`
builds the real graph with `ValidateOnBuild` to catch it. **Register new services there or that test fails.**

### UI specifics

- MVVM via CommunityToolkit.Mvvm; `ViewLocator` maps `FooViewModel` → `FooView` by name.
- **`MainWindow` caches every page view in a `Panel` and toggles `IsVisible`** — it does not swap a
  `ContentControl`. A swap rebuilds the view on every navigation, which reloads the Browse page's
  embedded WebViews from scratch. Don't "simplify" this back.
- `ModsPageViewModel` and `SettingsPageViewModel` are **singletons** because Settings mutates the Mods
  page's folder; the other page VMs are transient.
- Every view model keeps a parameterless constructor for the XAML previewer. `Noop*` stubs do nothing;
  `DesignTime*` stubs return fake data — not interchangeable.
- Dialogs are requested by a `ModsDialog` enum through `IDialogService` (view models don't know view
  types) and bind to `ModsPageViewModel` itself, not to dialog view models.
- The Browse page wraps `Avalonia.Controls.WebView` behind `IBrowsePageBrowser`, with per-platform
  bridges for ad blocking and download interception (`CoreWebView2` on Windows; raw Objective-C interop
  to WebKit on macOS, which is **unverified on real Mac hardware**).

## Conventions

From [.github/copilot-instructions.md](.github/copilot-instructions.md):

- **Never use `var`** — always explicit types.
- One class per file; no model types nested inside service classes.
- Use dependency injection wherever possible.
- Keep domain models out of Infrastructure.

Repo conventions:

- **Every feature gets an architecture doc** in `docs/architecture/` — context, what changed, a
  decisions table with reasons, and explicit "supersedes" links to docs it replaces. These are the
  fastest way into any subsystem; read the relevant one before changing it. `mods-page-redesign.md` is
  the most recent and supersedes parts of `mods-folder-ui.md` and `navigation-shell.md`.
- Test names are `Method_WhenCondition_ThenOutcome`. MSTest + Moq. Mock at the use-case boundary;
  assert against real IO in a temp sandbox at the infrastructure boundary.
- Bulk file operations return a list of `ModFileFailure` instead of throwing, and continue past
  per-file errors. There is no rollback. `AdoptAsync`/`AddToGroupAsync` are the exception: all-or-nothing.
- **Failure message strings are shown verbatim to the user** in the UI error bar — word them for a
  player, not a developer.
- `// ponytail:` comments mark deliberate shortcuts with their known ceiling and upgrade path. Grep
  for them before assuming something is an oversight.
- Every path written during extraction is validated to resolve under its target root (zip-slip guard).
  Keep that guard on any new extraction path.

## Version traps

- **FluentAvaloniaUI 3 renamed every type with an `FA` prefix.** Online samples showing
  `ContentDialog`, `NavigationView`, `InfoBar`, `Symbol` are v2 — here they are `FAContentDialog`,
  `FANavigationView`, `FAInfoBar`, `FASymbol`.
- Avalonia is **12.1**. Anything requiring `Avalonia.ReactiveUI 11.x` (notably CefGlue) will not work;
  the CefGlue browser path was removed for exactly this reason.
- Archives are zip-only — `System.IO.Compression` has no `.rar`/`.7z` support.
