# Mod Listing & Update Tracking — Direction

Status: **implemented**. All five phases below shipped (flat listing, folder-tree view,
install pipeline + records, adoption, manual groups); see
[flat-mod-listing-install-records.md](../flat-mod-listing-install-records.md) for what was
actually built against this direction, including a few decisions that got refined during
implementation (result types instead of exceptions, `IArchiveInstallService` scoped to
archives with adoption/groups living on `IModsFolderRepository` instead, etc.). This
document is kept as-is below as the original rationale — still accurate as the "why", even
where the "what" moved slightly during implementation.

## The decision

Stop inferring packages at discovery time. **Discovery lists files. Grouping is a user
action stored in the manifest. Updating is driven by install records, not by discovery.**

Three concerns that are currently tangled into one heuristic get separated:

| Concern | Today | Proposed |
|---|---|---|
| What is on disk | `DerivePackageKey` prefix guess | flat file list, no interpretation |
| What belongs together | same prefix guess | user-created groups (+ free folder grouping) |
| What to replace on update | nothing — archive overwrites by path | install record: the exact paths we wrote |

## Why the current model has to go

`ModsDiscoveryService.DerivePackageKey` takes the filename up to the first `_` or `-`.
Consequences observed in the current code:

- `MyMod_v2.package` and `MyMod_hair.package` merge into one mod; `WickedWhims.package`
  and `wickedwhims_tuning.package` may or may not, depending on punctuation.
- Identity is keyed on that guess. `ManifestMod.PackageKey` is the merge key in
  `DiscoverMods`, so a rename that changes the prefix **orphans the `ModId`** and a new
  Guid is minted. Any metadata attached to the old ID is silently lost.
- `IsMixedState` exists only to paper over bad grouping — a "mod" whose files are half
  enabled is almost always two unrelated files that got merged.
- `LoadModsAsync` writes the manifest on every read to persist those Guids. A list
  operation mutates state.

None of this survives contact with a real Sims 4 folder (thousands of files, arbitrary
author naming). Flat listing is what every established manager does for loose files, and
it is strictly less code.

## Proposed model

### Discovery output: one row per file

```
ModFile
  RelativePath : string      // normalized, '/' separated, root-independent — the identity
  State        : Enabled | Disabled   // derived from which root it was found under
  Size, ModifiedUtc, Extension
```

Key points:

- **`RelativePath` is the identity.** It is already stable across enable/disable —
  `ModsFileOperationsService` preserves the relative path when moving between
  `Mods` and `Mods.Disabled`. No Guid needed, no manifest write on read.
- `ManagedMod` / `PackageKey` / `IsMixedState` go away. `ManagedModFile` becomes the
  main type.
- Enable / disable / delete operate on a path (or a set of paths). Same file operations
  service, smaller inputs.
- **Discovery becomes pure.** Reads do not touch the manifest.

Trade-off: renaming a file outside the manager breaks the link to its stored metadata
(group membership, source). Accept it. Mitigation if it ever bites: opportunistic
SHA-256, computed only when a manifest row's path no longer resolves, matched against
unclaimed files. Do **not** hash the whole folder — a mods folder is routinely 10–60 GB.

### Grouping: user-owned, display-only

```
ModGroup
  GroupId : string
  Name    : string
  Members : string[]   // relative paths
```

- Purely cosmetic + bulk operations. Enable a group = enable each member. Nothing in
  discovery, file ops, or update logic reads groups.
- A file may belong to zero or one group (keep it simple; multi-group is a UI mess).
- Members whose path no longer exists render as "missing" rather than being dropped —
  the user's intent is worth more than a stale row, and it makes rename recoverable.

### Groups are virtual, not folders on disk

Grouping stores membership in the manifest and moves nothing. Reasons, in order of
weight:

- **It would break path-as-identity.** Moving a file changes its identity, so the
  operation most likely to be applied in bulk is also the one that invalidates every
  stored reference to its members — group membership, install record paths, user
  metadata. Grouping stops being a metadata write and becomes a filesystem transaction
  that has to be atomic with a manifest rewrite.
- **The folder is functional.** `.ts4script` only loads at depth ≤ 1. Nesting a script
  mod into a group folder silently breaks it, while packages (5 deep) keep working — so
  the breakage is selective and hard to diagnose, and the manager caused it.
- Moving tens of GB has a failure mode; annotating a manifest does not.

Note this is *not* inconsistent with enable/disable moving files between `Mods` and
`Mods.Disabled`. Hiding a mod from the game has no non-physical implementation, so it
earns the risk. Grouping has no functional requirement at all, so it does not.

The real cost of virtual: groups die with `%APPDATA%/ModManager/mods-manifest.json`, and
a user who uninstalls the manager keeps nothing. Address that by storing the per-folder
profile **inside the Mods folder** (the game ignores a stray `.json`) so the organization
travels with the mods — not by moving files.

Physical layout is still ours to pick at install time (one folder per mod, below). That
is not grouping; it is declining to make a mess. An explicit opt-in "organize on disk"
action can come later, and must refuse to place `.ts4script` deeper than one level.

### Free grouping before manual grouping

Before building a group editor, ship **grouping by folder** — a
`GroupBy(Path.GetDirectoryName)` and a tree view.

Caveat: this does nothing for a folder that is already flat. All files at root means one
group, which is the flat list again. Folder grouping is only as good as the structure on
disk, so it is not a substitute for grouping in the general case.

What makes it worth doing anyway is that **the structure is ours to create**. For
anything the manager installs, we choose the extraction layout: one folder per mod,
`Mods/<ModName>/...`, makes the folder tree true by construction rather than inferred.
The Sims 4 loader constrains this usefully — `.package` loads up to 5 levels deep,
`.ts4script` only 1 (directly in `Mods` or one folder down), so one folder per mod is
exactly the deepest layout that keeps scripts working.

So the division of labour is:

- **Installed by us** — folder grouping is real and is just the visible form of what the
  install record already knows. Nearly free.
- **Pre-existing flat pile** — folders carry no signal. Adoption is the only thing that
  groups these, and it is the same action that makes them updatable.

Sort/filter columns worth having on the flat list: name, folder, type, size, modified,
state, source, version. Search over filename covers the rest.

## Updating — the actual hard part

The question "which files do I replace?" is not answerable from the filesystem. It is
only answerable from **what we recorded when we installed**. So record it.

### Install record (the source of truth)

Written whenever the manager installs anything — browser download, archive extract,
or update:

```
InstallRecord
  InstallId    : string
  Source       : { Provider, ModPageUrl, DownloadUrl }
  Version      : string?        // as reported by the source, not sniffed
  InstalledUtc : DateTime
  Files        : [{ RelativePath, Sha256, SizeBytes }]
```

Update then becomes mechanical, no guessing:

1. Fetch the new archive for `Source`.
2. Compute the new path set from the archive.
3. Write new files (into the **disabled** root if the previous install's files were
   disabled — see bug below).
4. Delete paths present in the old record but absent from the new one.
5. Replace the record.

Step 4 is the part that does not work today and cannot work without a record.

### Bugs this fixes in the current update path

- `WickedWhimsArchiveInstaller.InstallArchive` extracts with `overwrite: true` and never
  removes the previous version's files. Any author who version-stamps filenames
  (`WickedWhims_v187a.package` → `v188b.package`) leaves both installed. The game loads
  both. That is a real corruption path, and it is invisible until something breaks.
- `WickedWhimsUpdateStrategy` writes to `request.ModsFolder` unconditionally. Updating a
  **disabled** mod silently re-enables it.
- `WickedWhimsVersionDetector.FindInstalledVersion` reads every `.package`/`.py`/
  `.ts4script` in the tree with `File.ReadAllBytes` and regexes the whole byte string.
  On a 40 GB folder that is a non-starter. With an install record the version is just
  read from the record; the sniffer is only needed for adoption.

### Files we did not install ("unmanaged")

Everything already in a user's folder. Three ways to handle, in order of cost:

1. **Leave them unmanaged.** They list, enable/disable, delete, group. They show no
   version and are not update-checkable. This is honest and is what most managers do.
2. **Adoption.** User selects one or more rows → "link to source" → picks the mod page /
   provider. That writes an `InstallRecord` with the current paths and a user-confirmed
   version. From then on it updates like anything else. This is the bridge, and it is
   the feature worth building.
3. **Per-mod detection strategies.** What `WickedWhimsVersionDetector` is. Keep the
   `IModUpdateStrategy` seam for the handful of mods big enough to justify bespoke code,
   but treat it as an escape hatch, not the model. Scope its scan to the record's paths
   once adoption has run.

### Where the version comes from

Prefer, in order: install record → provider API/page → filename/content sniffing.
Sniffing is last because it is expensive and wrong often enough to matter.

## Manifest reshape

`ManifestProfile.Mods` currently mirrors the filesystem. It should hold only what cannot
be re-derived from disk:

```
ManifestProfile
  ModsFolderPath : string
  Files          : [{ RelativePath, DisplayName?, GroupId?, Notes? }]   // sparse
  Groups         : ModGroup[]
  Installs       : InstallRecord[]
```

Sparse means: a file with no user metadata has **no row**. A fresh folder produces an
empty profile. Reads never write.

Migration from the existing manifest: nothing in it is worth keeping — the Guids are
internal and the names default to the derived key. Add a `SchemaVersion`, ignore and
overwrite anything older. Optionally, one-time convert each multi-file `PackageKey` into
a `ModGroup` so existing users keep their (accidental) grouping; low value, cheap to skip.

## Open questions

- **Same relative path in both roots.** Today it collapses into `IsMixedState`. With one
  row per file it is two rows with the same path, which breaks path-as-identity. Needs a
  rule: surface as a conflict, prefer enabled, and offer a "resolve" action. Rare, but
  `MoveModFilesForStateChange` already refuses to overwrite, so it can happen and stick.
- **Case sensitivity.** Everything compares `OrdinalIgnoreCase` today. Fine for Windows;
  fix the comparer choice at the model boundary if macOS/Linux is ever real.
- **Multi-file archives that the user then reorganizes.** Moving files after install
  invalidates the install record's paths. Detect on update (path missing), and either
  prompt or fall back to hash matching over just that record's files — bounded work.
- **`.ts4script` depth rule.** Worth surfacing as a warning in the list (script nested
  more than one folder deep will not load). Cheap, and users hit this constantly.
- **Does the disabled-sibling-folder approach stay?** Not in scope here, but note that
  it makes relative path the natural identity, which the whole proposal leans on.

## Suggested order of work

1. Flat listing: `ModFile` replaces `ManagedMod`, discovery goes pure, manifest write
   drops out of the read path. Delete `DerivePackageKey`, `PackageKey`, `IsMixedState`.
2. Folder-tree view + sort/filter/search on the flat list.
3. Install records written on browser-download install, extracting one folder per mod.
   Update path deletes stale files.
4. Adoption flow for pre-existing files.
5. Manual groups — needed for a flat legacy folder, where folder grouping gives nothing.
   Defer until adoption is in, since adoption may cover enough of it.

Steps 1, 3 and 4 carry the weight. Step 2 is close to free once step 1 lands but only
pays off on folders with structure; step 3 is what creates that structure.
