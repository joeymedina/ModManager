# Mod Categories

## Context

Mods had no way to be classified by type. The app already has a **Group** feature
(`GroupId` on `ModFile`/`ManifestFileEntry`, a top-level `ModGroup` list with named,
multi-member containers — see [mods-folder-service.md](./mods-folder-service.md)) but it's a
freeform, user-named organizational tool, not a taxonomy of what a mod *is*.

This adds a second, independent **Category** field: one category per mod, drawn from a
suggested list (`ModCategories.Suggested`: Scripts, CAS, Build/Buy, Gameplay,
Overrides/Recolors, Poses/Animations, Traits/Careers, UI) but freely overridable with a custom
value. It's settable at install time, settable/editable on already-installed mods via a bulk
action, and used to filter the mod list. A mod can have both a Group and a Category — they're
unrelated.

This complements [mods-folder-service.md](./mods-folder-service.md) (backend plumbing) and
[mods-page-redesign.md](./mods-page-redesign.md) (UI shell) rather than superseding either.

## What changed

- **`ManifestFileEntry`** and **`ModFile`** (Application/Models) each gained a trailing
  `string? Category = null` parameter. Trailing placement keeps every existing positional
  constructor call valid.
- **`ModCategories`** (Application/Models) is a new static class holding the suggested list.
  Not an enum — any string is a valid category, matching how Group names work.
- **`IModsFolderUseCase`/`IModsFolderRepository`** gained `SetCategoryAsync(modsFolderPath,
  relativePaths, category, cancellationToken)`, implemented by `ModsFolderService`. Passing
  `null` or a blank string clears the category.
- **`IArchiveInstallService.InstallAsync`** gained a `string? category` parameter, threaded
  into the manifest entry written for each installed file.
- The UI selection bar gained a "Category…" button (mirrors "Group…"), the Install dialog
  gained an optional Category field, the find bar gained a category filter dropdown, and each
  mod row shows a category badge when set.

## Decisions

| Decision | Reason |
| --- | --- |
| A separate field from Group, not a repurposed Group | Group is a freeform, user-named, multi-member container; Category is a single, suggested-but-open classification of what the mod is. Conflating them would mean losing one concept to gain the other. |
| No `ModCategory` record / no top-level list in the manifest | Category has no membership semantics — it's a plain field per file, not a many-member container needing id-minting and case-insensitive name reuse the way `ModGroup` does. |
| Blank input clears the category (no validation error) | Unlike a group name, which is meaningless if blank, an empty category unambiguously means "uncategorized" — no separate "remove category" action is needed. |
| `RemoveFromGroupAsync`'s manifest-entry prune check now also tests `Category is not null` | Before this change, removing a file from a group would silently drop its `ManifestFileEntry` (and therefore its category) once `GroupId` went back to null, if `DisplayName`/`Notes` were also unset. The same four-field check now guards `SetCategoryAsync` too. |
| Suggested list lives in Application, not Ui | Both the Install dialog and the bulk Set Category dialog need it, and a future CLI category flag could reuse it without depending on Ui. |
