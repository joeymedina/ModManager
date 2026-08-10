# Theming

## Context

Users can now customize the app's colors and typography from the Settings page: pick a
built-in theme, edit one live in a dialog, duplicate it, or import/export it as a JSON file.
Nothing in `ModManager.Application` or `ModManager.Infrastructure` changed; this is entirely a
`ModManager.Ui` feature, following the same pattern `SettingsStore` set for UI-local
persistence.

Related documentation: [mods-page-redesign.md](./mods-page-redesign.md) for the dialog and
`IDialogService` pattern this feature extends.

## The mechanism theming rests on

Every view already binds colors with `DynamicResource`, not hardcoded hex or `StaticResource`
(confirmed by grepping the views before writing any code). That single fact is what makes
theming cheap: applying a theme means overriding named Fluent resource keys at the
`Application` level, not touching XAML.

Two things about Avalonia's resource system were verified live (via a throwaway runtime probe
in `App.axaml.cs`, removed before committing) rather than assumed:

1. **A flat key in `Application.Resources` wins over `FluentAvaloniaTheme`'s
   variant-scoped `ThemeDictionaries` entry for the same key, regardless of the current
   `RequestedThemeVariant`.** This is what lets one palette (`AppTheme.IsDark` picks the base,
   but every color is still explicit) replace the light/dark toggle instead of following it.
2. **Fluent defines each color as a pair** — `FooColor` (a `Color`) and `FooColorBrush` (a
   `SolidColorBrush` wrapping it). Overriding only the `Color` key leaves the `Brush` key,
   which most controls actually bind to, unchanged. `ThemeService.WriteColor` always writes
   both.

`ThemeService.Apply` also sets `FluentAvaloniaTheme.CustomAccentColor`, which derives the six
accent shades on its own. No manual ramp needed.

## Architecture

```text
┌──────────────────────────────────────────────────────────────────────┐
│ SettingsPageView                                                     │
│  Theme picker (ComboBox) + Edit… / Duplicate / Delete / Import… /    │
│  Export… / Reset to default                                          │
└───────────────────────────┬────────────────────────────────────────┘
                            │ DataContext
┌───────────────────────────▼────────────────────────────────────────┐
│ SettingsPageViewModel                                                │
│  AvailableThemes: ObservableCollection<AppTheme>, SelectedTheme      │
│  On SelectedTheme change → ThemeService.Apply + persist ThemeName    │
│  EditThemeCommand → builds a ThemeEditorViewModel draft, shows it    │
└─────────────┬───────────────────────────────────────┬────────────────┘
              │                                       │ AppDialog.ThemeEditor
              │                          ┌────────────▼────────────────────┐
              │                          │ ThemeEditorDialogContent          │
              │                          │  10× ColorPicker, font family/    │
              │                          │  size, contrast warning           │
              │                          └────────────┬────────────────────┘
              │                                       │ DataContext
              │                          ┌────────────▼────────────────────┐
              │                          │ ThemeEditorViewModel               │
              │                          │  Discardable draft. Every edit    │
              │                          │  calls ThemeService.Apply live —  │
              │                          │  the dialog previews itself.      │
              │                          └────────────┬────────────────────┘
              │                                       │
┌─────────────▼───────────────────────────────────────▼────────────────┐
│ ThemeService (singleton)                                              │
│  Apply(AppTheme) → Application.Resources overrides + CustomAccentColor│
│  List / Save / Delete / Import / Export — JSON files                  │
└─────────────┬───────────────────────────────────────────────────────┘
              │
┌─────────────▼───────────────────────────────────────────────────────┐
│ ThemePresets (static, not files)      AppTheme (record, hex strings) │
│  Default Light / Default Dark / Plumbob                              │
└────────────────────────────────────────────────────────────────────┘
```

### New types

| Type | Role |
| --- | --- |
| `AppTheme` | The palette + typography record. Colors are ARGB hex strings so it round-trips through JSON with no custom converter. |
| `ThemePresets` | Three built-in `AppTheme` values, hardcoded — not files on disk, never overwritable or deletable. |
| `ThemeService` | Applies a theme to the live app; lists, saves, deletes, imports, and exports custom ones under `%APPDATA%\ModManager\themes\*.json`. |
| `ThemeEditorViewModel` | A discardable draft used only by the editor dialog. |
| `ThemeEditorDialogContent` | The editor dialog body — 10 `ColorPicker`s (from the already-transitive `Avalonia.Controls.ColorPicker` package; no new dependency), a font family box, a font-size spinner. |

### Changed types

| Type | Change |
| --- | --- |
| `AppSettings` | Gained `ThemeName`. |
| `SettingsPageViewModel` | Owns theme selection, the five theme commands, and `EditThemeCommand`. |
| `ModsDialog` → `AppDialog` | Renamed — the enum now hosts a non-mods dialog too. Mechanical rename across `IDialogService`, `DialogService`, `ModsPageViewModel`, and their tests. |
| `IDialogService.PickFileAsync` | Gained a `filterLabel` parameter (was hardcoded `"Mod files"`). |
| `IDialogService` | Gained `SaveFileAsync` for theme export. |
| `App.axaml.cs` | Applies the persisted theme (or Default Light) before the window is created. |

## Design decisions

| Decision | Reason |
| --- | --- |
| Ten curated color slots, not a full ~200-key Fluent token editor | Covers everything a user would actually notice; a full editor would be a spreadsheet nobody wants to fill in |
| Typography limited to font family + base size | "Text" in the request meant look-and-feel, not editable UI wording — that's a localization-shaped feature, out of scope |
| One palette per theme, not a light/dark pair | Selecting a custom theme replaces the light/dark toggle rather than following it; half the editing UI and half the data model |
| Settings-page card + dialog, not a dedicated "Themes" nav page | Used rarely; reuses the existing `IDialogService` pattern instead of adding a nav entry and a page VM |
| Theme model and `ThemeService` live in `ModManager.Ui`, not the `Application`/`Infrastructure` spine | A theme has no meaning to the CLI or the mods domain — same precedent `SettingsStore` already set. An `IThemeUseCase` → `IThemeRepository` spine would be an interface with exactly one implementation |
| Export/import as JSON files | Lets users share themes; less code than any in-app-only sharing mechanism |
| A few built-in presets, not a blank slate | Presets double as the "duplicate this and edit it" starting point, which is how most people actually make a theme |
| Reset-to-default always visible in the Settings card (not only inside the dialog) | A theme that hides its own dialog buttons still leaves an escape hatch |
| Non-blocking WCAG contrast warning, not a hard block | Warns about an unreadable theme without fighting a user who knows what they want |
| Dialog binds to a dedicated `ThemeEditorViewModel`, unlike the mods dialogs (which bind to `ModsPageViewModel` itself) | Cancel needs a discardable draft with 14 properties; forcing that onto `SettingsPageViewModel` directly would pollute it the way the mods dialogs' small forms don't need to |

## How Save/Cancel work in the editor

Every property change on `ThemeEditorViewModel` calls `ThemeService.Apply` immediately, so the
live app, dialog included, previews the edit as it happens. Nothing is written to disk until
Save.

- **Cancel** re-applies the theme that was active before the dialog opened, undoing the preview.
- **Save** validates the name isn't blank, then calls `ThemeService.Save`. If the name still
  collides with a builtin (the editor seeds a `"Copy of X"` name when editing a builtin, but a
  user can retype the exact builtin name), `Save` throws and the view model catches it into a
  status message on the Settings page. That follows the same post-close-validation pattern the
  Adopt and Add-to-group dialogs already use, rather than gating the Save button.

## Gotchas found while building this

Recorded because each cost real debugging time and none is obvious from the code.

**`NumericUpDown.Value` is `decimal?`, not `double`.** Binding it directly to a `double`
`FontSize` left a persistent `DataValidationErrors` adorner that visually collided with the
spin buttons at a narrow width. `ThemeEditorViewModel.FontSize` is `decimal` to match the
control exactly; the conversion to/from `double` happens only at the `AppTheme` boundary,
since `AppTheme.FontSize` stays `double` to match the FluentAvalonia resource it feeds.

**Removing a `ComboBox`'s selected item from its bound `ItemsSource` nulls `SelectedItem`.**
`DeleteThemeAsync` used to remove the deleted theme from `AvailableThemes` and *then* select
the fallback, but Avalonia's `ComboBox` reacts to the removal by nulling `SelectedItem`. That
round-trips through the two-way binding into `SelectedTheme = null`, and `ThemeService.Apply`
threw on the null. Fixed by selecting the fallback *before* removing the old theme (so it's no
longer the current selection when it's removed), plus a defensive null guard in
`OnSelectedThemeChanged`: the `AppTheme` type says non-null, but Avalonia's binding engine
doesn't enforce that at runtime, and a future collection mutation under a live selection could
hit the same trap.

**`SettingsStore.Save` call sites must round-trip through `Load()`.**
`ModsPageViewModel.SetModsFolderAsync` used to call
`_settings.Save(new AppSettings { ModsFolderPath = ... })`, constructing a fresh record and
silently dropping every other field. That was harmless before `ThemeName` existed, and a live
bug the moment it did. Fixed to `_settings.Save(_settings.Load() with { ModsFolderPath = ... })`. Any
future field on `AppSettings` needs every existing write site checked the same way.

**A single `OnPropertyChanged` override beats ten duplicate `On*Changed` partials.**
`ThemeEditorViewModel` has ten color properties that all need the same reaction (live-apply +
recompute the contrast warning). One `protected override void OnPropertyChanged` does it for
all of them, guarded on `e.PropertyName == nameof(ContrastWarning)` so the warning's own
change notification can't re-enter the override forever.

## Files touched

**Added**

- `ModManager.Ui/Models/AppTheme.cs`
- `ModManager.Ui/Services/ThemePresets.cs`, `ThemeService.cs`
- `ModManager.Ui/ViewModels/ThemeEditorViewModel.cs`
- `ModManager.Ui/Views/Dialogs/ThemeEditorDialogContent.axaml` / `.cs`
- `ModManager.Tests/Ui/ThemeServiceTests.cs`
- `ModManager.Tests/Ui/ViewModels/ThemeEditorViewModelTests.cs`
- `docs/architecture/theming.md` (this document)

**Updated**

- `ModManager.Ui/Services/SettingsStore.cs`: added `AppSettings.ThemeName`
- `ModManager.Ui/Services/IDialogService.cs`, `DialogService.cs`: `ModsDialog` → `AppDialog` (+ `ThemeEditor` case), `PickFileAsync` filter label, `SaveFileAsync`
- `ModManager.Ui/ViewModels/SettingsPageViewModel.cs`: theme list, selection, five theme commands, `EditThemeAsync`
- `ModManager.Ui/ViewModels/ModsPageViewModel.cs`: `AppDialog` rename; `SetModsFolderAsync` settings-overwrite fix
- `ModManager.Ui/Views/SettingsPageView.axaml`: Theme card
- `ModManager.Ui/App.axaml.cs`: applies the persisted theme at startup
- `ModManager.Ui/Extensions/ServiceCollectionExtensions.cs`: registers `ThemeService`
- `ModManager.Tests/Ui/ViewModels/ModsPageViewModelTests.cs`, `SettingsPageViewModelTests.cs`: rename fallout + new coverage

## Out of scope

- Editable UI wording (localization-shaped; a much larger feature than typography)
- Light/dark pairs per theme (one palette replaces the toggle instead)
- Per-page themes
- CLI theming
- Corner radius, spacing, or any layout-level customization
- Browse page web content: the chrome around the embedded `WebView` themes, but third-party page content does not
- Hard-blocking unreadable color combinations (the contrast warning is advisory only)

## Verification

- `dotnet build ModManager.slnx`: clean, no warnings.
- `dotnet test ModManager.slnx`: 164 passing, 32 new (`ThemeServiceTests`,
  `ThemeEditorViewModelTests`, and the theme-related additions to `SettingsPageViewModelTests`).
- Manual: launched the real exe after every phase and confirmed clean startup with no errors in
  `%LOCALAPPDATA%\ModManager\logs\app-.log`. The live resource-override mechanism (flat key
  beats `ThemeDictionaries`, both `Color` and `Brush` keys required) was confirmed with a
  throwaway runtime probe before writing `ThemeService`, not assumed.
- **Not verified**: pixel-level interaction with the running app (dragging a `ColorPicker`,
  clicking through the editor dialog). `ModManager.Ui.exe` isn't a Start-Menu-registered
  application, so computer-use's allowlist rejects it outright. That gap is a tooling
  limitation, not a skipped step. Run `dotnet run --project ModManager.Ui`, open Settings, and
  click Edit… to confirm visually.

If the output folder is locked by a running instance, build elsewhere with
`-p:ArtifactsPath=<dir>` rather than killing the app.
