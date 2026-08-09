# Navigation Shell Architecture

## Context

The previous state of the UI exposed a single mods-management page directly in `MainWindow`. This change introduces a multi-page navigation shell so that Mods, Updates, and Browse pages can coexist under a single window with a sidebar navigation rail.

Related page documentation: [mods-folder-ui.md](./mods-folder-ui.md), [browse-page.md](./browse-page.md).

## What Changed

### Navigation model

- `MainViewModel` now owns a list of `NavigationItemViewModel` instances, each pairing a display label with a page view model.
- The selected navigation item drives `CurrentPage`, which the main window binds to its content area.
- Page switching is handled entirely in the view model layer; the view observes `CurrentPage` through a `ViewLocator` that maps view model types to their corresponding Avalonia views.

### Pages added

| Label | ViewModel | Initial state |
| --- | --- | --- |
| Mods | `ModsPageViewModel` | Default active page |
| Updates | `UpdatesPageViewModel` | Placeholder (future `IModUpdateOrchestrator` integration) |
| Browse | `BrowsePageViewModel` | Embedded browser with download support |

### MainWindow updated

- `MainWindow` no longer hosts a single page directly.
- It hosts the navigation rail and a `ContentControl` bound to `CurrentPage`.
- `ViewLocator` resolves the correct Avalonia view for each page view model at runtime.

## Architecture

```text
┌───────────────────────────────────────────────────────────────┐
│ MainWindow (Avalonia)                                         │
│  ├─ Navigation rail (binds NavigationItems)                   │
│  └─ ContentControl  (binds CurrentPage via ViewLocator)       │
└────────────────────────────┬──────────────────────────────────┘
							 │
┌────────────────────────────▼──────────────────────────────────┐
│ MainViewModel                                                 │
│  NavigationItems: NavigationItemViewModel[]                   │
│  SelectedNavigationItem  (drives CurrentPage)                 │
│  CurrentPage: ViewModelBase                                   │
│   ├─ ModsPageViewModel     -> ModsPageView                    │
│   ├─ UpdatesPageViewModel  -> UpdatesPageView                 │
│   └─ BrowsePageViewModel   -> BrowsePageView                  │
└───────────────────────────────────────────────────────────────┘
```

## MVVM Structure

| Type | Role |
| --- | --- |
| `MainViewModel` | Shell VM; owns navigation item list, selected item, and current page |
| `NavigationItemViewModel` | Associates a display label with a page VM |
| `ViewModelBase` | Shared base for all page view models (`ObservableObject` from CommunityToolkit.Mvvm) |
| `ViewLocator` | Maps `ViewModelBase` subtypes to matching Avalonia view types by naming convention |
| `MainWindow` | Hosts navigation rail and content area; `DataContext = MainViewModel` |

### Page navigation flow

1. User selects a `NavigationItemViewModel` in the rail.
2. `MainViewModel.OnSelectedNavigationItemChanged` sets `CurrentPage` to the selected item's page VM.
3. Avalonia `ContentControl` re-evaluates the `DataTemplate` from `ViewLocator`.
4. The matching page view is instantiated and displayed.

Page view models are long-lived (transient DI lifetime but kept in `NavigationItems` for the application lifetime). No state is lost when switching pages and returning.

## Dependency Injection

Registered in `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`:

| Type | Lifetime | Note |
| --- | --- | --- |
| `ModsPageViewModel` | Transient | Injected into `MainViewModel` constructor |
| `UpdatesPageViewModel` | Transient | Injected into `MainViewModel` constructor |
| `BrowsePageViewModel` | Transient | Injected into `MainViewModel` constructor |
| `MainViewModel` | Transient | Constructed by DI; assigned as `MainWindow.DataContext` |
| `BrowserDownloadService` | Singleton | Shared across the application lifetime |

`App.OnFrameworkInitializationCompleted` builds the service provider and resolves `MainViewModel`. All lower-layer services (`IModsFolderUseCase`, `IModsFolderRepository`, helpers) are already registered by `AddApplicationServices` and `AddInfrastructureServices`.

## Design-Time Support

`MainViewModel()` and each page view model expose parameterless constructors for the Avalonia XAML previewer. These constructors wire up lightweight stubs or empty state without requiring a real service graph.

## Files Touched

### Added

- `ModManager.Ui/ViewModels/NavigationItemViewModel.cs`
- `ModManager.Ui/ViewModels/UpdatesPageViewModel.cs`
- `ModManager.Ui/ViewModels/BrowsePageViewModel.cs`
- `ModManager.Ui/Views/UpdatesPageView.axaml` / `.cs`
- `ModManager.Ui/Views/BrowsePageView.axaml` / `.cs`
- `docs/architecture/navigation-shell.md` (this document)

### Updated

- `ModManager.Ui/ViewModels/MainViewModel.cs`
- `ModManager.Ui/Views/MainWindow.axaml` / `.cs`
- `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`

## Out of Scope

- Breadcrumb or back-navigation within a page
- Deep-link routing or URL-based navigation
- Animated page transitions
- Persisting the last-selected navigation item across sessions
