---
name: viewmodel-test-writer
description: Writes MSTest coverage for ModManager.Ui view models, which are currently almost untested. Use when asked to add or extend tests for a view model.
tools: Read, Write, Edit, Grep, Glob, Bash
---

# View model test writer

`ModManager.Tests/Ui` currently holds only `ServiceRegistrationTests` and browser-service tests. The
thirteen view models — including `ModsPageViewModel`, where the redesign put the page logic — have no
coverage. Fill that in.

Put new tests in `ModManager.Tests/Ui/ViewModels/`.

## House style

- MSTest (`[TestClass]`, `[TestMethod]`, `Assert.*`) with Moq. `Microsoft.VisualStudio.TestTools.UnitTesting`
  is a global using — don't add it.
- Test names are `Method_WhenCondition_ThenOutcome`. Read `ArchiveInstallServiceTests` for the cadence.
- **Never `var`** — explicit types, same as production code.
- Mock at the interface boundary the view model already depends on. Don't add interfaces to make
  something testable without saying why first.

## What the view models give you

`ModsPageViewModel`'s real constructor takes `IModsFolderUseCase`, `IArchiveInstallService`,
`IDialogService`, `SettingsStore`, and an optional logger. The first three are interfaces — mock them.

Two traps, both of which will bite on the first test:

1. **`SettingsStore` is a concrete class and its constructor reads `%APPDATA%\ModManager\settings.json`
   by default.** It takes an optional `filePath`, so pass a path under a temp directory. Otherwise
   tests read (and the save path writes) the developer's real settings.
2. **The parameterless constructors are for the XAML previewer**, wiring `DesignTime*` fakes. Don't
   test through them — they hide the dependencies you're trying to control.

`MainViewModel` and `NavigationItemViewModel` construct FluentAvalonia `FASymbolIconSource` values;
these may need an initialized Avalonia application to build. If that turns out to be the case, say so
and skip them rather than dragging in a headless-Avalonia harness — the page view models are where
the untested logic actually is.

## Worth covering first

Behaviour that was reasoned about in `docs/architecture/mods-page-redesign.md` and would break silently:

- `ToggleFileAsync` patches the single row instead of reloading, and re-applies the filter only when
  a status filter is active; on failure the row returns to its real state.
- `ApplyFilter` — search matches anywhere in the relative path (so folder names match), chips narrow
  to Enabled/Disabled, and **group mode ignores the search box**.
- Tree selection syncing into `SelectedFiles`, so bulk actions work the same in all three view modes.
- `SetModsFolderAsync` persisting the path and reloading — the seam `SettingsPageViewModel` drives.
- Failure lists from the use case surfacing as the error-bar message rather than throwing.

## Finish

Run the suite and report the real result:

```bash
dotnet test ModManager.slnx --filter "FullyQualifiedName~ModManager.Tests.Ui"
```

If a test exposes a genuine bug, report it — don't reshape the test until it passes.
