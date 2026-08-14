# Site-based update checking

## Status

**In progress, functionally end-to-end for Sacrificial.** The shared file-operation groundwork, the
site-strategy base spine, `SacrificialSiteStrategy`, and the Updates page are implemented and tested
(see [Implementation status](#implementation-status)). The gap that remains: a *fresh* install doesn't
get tracking set automatically yet, only adoption does — so today's path is install, then adopt to
link it to a site. Sections below describe both what shipped and what's still a decision to confirm,
distinguished where it matters.

## Context

Update checking today is *mod*-keyed: `IModUpdateOrchestrator` dispatches by `ModId` to a registered
`IModUpdateStrategy`, and exactly one strategy exists (`WickedWhimsUpdateStrategy`). That works for a
handful of mods big enough to justify bespoke code, and the CLI is built on it. It does not scale to
"tell me which of my 200 installed mods have updates," because there is no per-mod code to write for
200 mods.

This feature adds a second, *site*-keyed spine. The unit of work stops being "a mod we wrote code
for" and becomes "an install record whose mod page lives on a site we can read." One strategy per
site covers every mod hosted there.

Relationship to existing docs:

- **Extends, does not supersede, [mod-update-orchestrator.md](./mod-update-orchestrator.md).** The
  mod-keyed `IModUpdateStrategy` and `WickedWhimsUpdateStrategy` stay exactly as they are; the CLI
  keeps consuming them unchanged. The two spines coexist.
- **Realizes the "Updating" and "Adoption" sections of
  [mod-listing-and-update-tracking.md](./mod-listing-and-update-tracking.md).** That document
  established install records as the source of truth for updates and named adoption as "the bridge,
  and the feature worth building." This is that bridge.
- Touches the install path described in
  [flat-mod-listing-install-records.md](../flat-mod-listing-install-records.md).

## Scope

**v1 is deliberately one site, detect-only.**

| In scope | Out |
| --- | --- |
| Sacrificial Mods, `downloads.html` only | Sacrificial Jr. / Kyutso / Sacricolors pages |
| Detecting that an update exists | Downloading or applying it automatically |
| Manual "Check for updates" button | Check on app launch, background sweep |
| Adoption by pasting a mod page URL + typing a version | Picking a mod from a fetched site index |
| Record supersession on reinstall (see below — v1 cannot ship without it) | — |

Sites planned after Sacrificial, in no fixed order: ModTheSims, CurseForge, Patreon, NexusMods,
TheSimsResource. Each is its own increment; the interfaces below are shaped so adding one is a new
class plus a DI registration.

## The site: Sacrificial Mods

Everything v1 needs is on one static page, [sacrificialmods.com/downloads.html](https://sacrificialmods.com/downloads.html):

- Displayed version, e.g. `v2.6.3.2`
- `Last Update: 10-12-2025` (MM-DD-YYYY, absolute — not a relative "3 days ago")
- A direct `.zip` link with the version in the filename:
  `SAC_ExtremeViolence%20-MOD-%20V2.6.3.2.zip`
- **A per-mod anchor id**: `downloads.html#PassionateRomanceDownload`

No authentication, no Cloudflare interstitial. Plain HTTP works.

The anchor is what makes this site the right starting point. It gives the record a stable in-site
identity *and* gives the parser a precise contract — locate `#<anchor>`, read version/date/zip from
its containing section — rather than fuzzy-matching a mod title against a page. A missing anchor is
an unambiguous "couldn't determine" instead of a silently wrong match.

One page covers every installed Sacrificial mod, so a full check for this site is **one HTTP
request** regardless of how many of its mods are installed.

### Confirmed against a real install record

A live record for Zombie Apocalypse settles what was previously guesswork:

```json
"Source": {
  "Provider": "browser",
  "ModPageUrl": "https://sacrificialmods.com/downloads.html#ZombieApocalypseDownload",
  "DownloadUrl": "https://sacrificialmods.com/Direct_Mod_Downloads/SAC_Zombie Apocalypse  -MOD- v2.3.1.zip"
},
"Version": null
```

- **The fragment survives the download interception.** `ModPageUrl` carries
  `#ZombieApocalypseDownload`, so the anchor is available as the in-site mod key on records we
  already have.
- **The download URL is a literal `sacrificialmods.com` `.zip`, not a redirect or CDN hop**, and it
  carries the version (`v2.3.1`). Combined with the anchor, **every Sacrificial mod already installed
  through the browser can be backfilled with no user input** — key from `ModPageUrl`, version from
  `DownloadUrl`. `Version` being `null` today costs nothing.
- URLs contain **unencoded literal spaces and doubled spaces** (`SAC_Zombie Apocalypse  -MOD-`).
  Parsers must tolerate irregular whitespace rather than assuming a clean token.
- File extensions appear in **mixed case** (`.Package` alongside `.package`). Every path comparison
  stays `OrdinalIgnoreCase`, as `DeleteStaleFiles` and `ModExtensions` already are.

### Version sniffing is confirmed unworkable on this site

The same record contains `SAC_Zombie Apocalypse -MOD- v1.0 Animations.package` and
`v2.0 Animations.package` next to the `v2.3.1` files — **asset version numbers that are not the mod
version**. Any regex over filenames finds three candidates and has no principled way to pick. Version
comes from the download URL or the page, never from sniffing installed files. This is the concrete
justification for the ordering [mod-listing-and-update-tracking.md](./mod-listing-and-update-tracking.md)
already proposed: install record → provider page → sniffing, with sniffing last.

### Confirmed against the live page's actual markup

The install record above proved the fragment survives; fetching `downloads.html` directly (implemented
and tested — see [Implementation status](#implementation-status)) proved what it points at. Every mod
is one flat `<div class="mod-card" id="ZombieApocalypseDownload" data-search-title="Zombie Apocalypse" …>`,
no nesting, with the site's own "copy mod link" button using that same `id` as the shareable URL
fragment:

```html
<div class="mod-card" id="ZombieApocalypseDownload" data-search-title="Zombie Apocalypse" …>
  <div class="mod-actions">
    <a href="https://sacrificialmods.com/Direct_Mod_Downloads/SAC_Zombie...v2.3.1.zip" class="btn btn-primary" …>Download</a>
    <a href="https://www.patreon.com/file?...acutal" class="btn btn-patreon" …>Download</a>
    <a href="https://sacrificialmods.com/zombie-apocalypse-news.html" class="btn btn-secondary" …>Release Notes</a>
  </div>
  <div class="mod-image-container">
    <span class="version-badge">v2.3.1</span>
  </div>
  <div class="update-info-column">
    <div class="update-info-item">Last Update: <span><B>09-7-2025</B></span></div>
  </div>
</div>
```

This corrects two things assumed earlier:

- **The `id` is used unmodified as the mod key — no suffix-stripping.** The pattern is PascalCase +
  `Download` as guessed, but since the site's own id already is the mod's canonical identity (its copy-
  link button targets this exact string), transforming it before use would only make resolve-time and
  parse-time keys diverge for no benefit. Simpler than planned.
- **`data-search-title` is the title source, not the visible `<h3>`.** One real card's heading reads
  "Path Of Legends By Kyutso" while `data-search-title` reads "Path Of Legends" — the attribute is the
  clean, site-maintained value; the heading is decorated for display.

Two more things the real markup settled: **`Last Update` is not zero-padded** (`09-7-2025`, day `7`
not `07`), reinforcing that the raw string must never be parsed as a date, only compared for equality.
And **every card carries two download links** — `btn btn-primary` (direct from SacrificialMods.com)
and `btn btn-patreon` (an alternative) — so the parser must key off the `btn-primary` class
specifically, not just "the first download-looking link," or it risks quietly preferring Patreon.

### Still open

When a user scrolls to a mod's section rather than clicking the top-of-page link, the address bar
carries no fragment. That record resolves to "site known, mod unknown" and prompts for a link rather
than guessing — the same unresolved state adoption uses, and it is retried on every check. This path
itself is still unverified against a real click-vs-scroll download; the fixture tests below use the
URL directly rather than reproducing the browser interaction.

## Model

### Comparison semantics

**Inequality, not ordering.** Version schemes across the seven target sites are irreconcilable —
`187a`, SemVer, `v3 patch 2`, `2024-05-12`, `Build 47`. Any general comparer is wrong on some of
them, and wrong in the dangerous direction (silently reporting "up to date"). So the rule is: the
value on the page differs from the value we recorded → update available. A strategy may opt into
true ordering later where its scheme is confirmed.

Both sides are normalized before comparison: trim, strip a leading `v`/`V`, compare
case-insensitively. Without this, a user typing `2.6.3.2` against a page showing `v2.6.3.2` gets a
false positive they cannot clear.

**Dates are compared as raw strings, never parsed.** The stored value is the literal `10-12-2025`.
Change detection only needs "did this differ from what I recorded," which sidesteps MM-DD/DD-MM
ambiguity and timezone entirely. Parsing is only needed if we later want to *display* "released 3
days ago."

**Version takes precedence over date.** When a comparable version exists on both sides, the date is
ignored — it is the noisier signal (sites bump it for description edits and re-uploads). The date is
a fallback for sites with no version, and produces the same `UpdateAvailable` state.

### Three outcomes, not two

A check yields `UpToDate`, `UpdateAvailable`, or **`Indeterminate`** with a reason. The third is not
optional: scrapers break silently when a site redesigns, and a parser that returns "no version
found" is indistinguishable from "up to date" unless the model can say so. `Indeterminate` surfaces
in the UI as a warning, not as silence.

This is why `ModUpdateResult` cannot be reused as-is: its `InstalledVersion` is non-nullable and its
`VersionComparison` is a bare `int`, neither of which can express "unknown."

### Where state lives

Two different lifetimes, two different homes:

| What | Where | Why |
| --- | --- | --- |
| **Baseline** — site key, in-site mod key, tracking URL, observed version, observed update-date string, when captured | `InstallRecord` in `.modmanager.json` inside the Mods folder | Written rarely (install, adoption, "mark as current"). Belongs with the install it describes, and travels with the Mods folder like all other user metadata. |
| **Check state** — last checked, last seen values, last error | A cache under `%LOCALAPPDATA%\ModManager\` | Re-derivable, rewritten on every check. Putting it in the Mods folder means a background sweep churns a file inside the user's 40 GB folder for data nobody needs to keep. |

Baseline fields are added as a nullable block on `InstallRecord`, so existing manifests deserialize
to `null` with no migration and no schema bump.

**Baseline is separate from `InstallRecord.Version`** even though for Sacrificial the two strings are
usually identical. `Version` is history — what was installed. The baseline is what we compare
against, and "mark as current" rewrites it. Keeping them separate means clearing a false positive
does not falsify the install history.

Likewise the **tracking URL is separate from `InstallSource.ModPageUrl`**. `ModPageUrl` is
provenance — where this archive actually came from. The tracking URL is user-correctable (the
address-bar capture is often the wrong page, and the anchor is often missing). Re-pointing where we
check should not rewrite where it came from.

## Interfaces

```text
IModSiteStrategy                                  (Application)
  SiteKey       : string                          // e.g. "sacrificialmods.com"
  Hosts         : IReadOnlyList<string>           // hosts that route here
  Capabilities  : SiteCapabilities                // auth needed? dates provided?
  TryResolveModKey(ModKeyHints) -> SiteModKey?    // URL fragment, download URL, display name
  FetchObservationsAsync(IReadOnlyList<SiteModKey>, ct)
                -> IReadOnlyList<SiteObservation> // BATCH — see below

SiteObservation                                   (Application)
  SiteModKey, Version?, UpdatedOnRaw?, Title?, DownloadUrl?, ModPageUrl?

IModPageFetcher                                   (Application)
  FetchAsync(Uri, ct) -> Task<PageContent>
```

### A strategy reports observations; it never renders a verdict

An earlier draft had `CheckAsync` return `SiteCheckResult` — the verdict — from the strategy. That
was the wrong seam. It would make every site author re-implement version normalization, the
version-over-date precedence, and the three-state outcome, and those implementations would drift
apart the moment a second site existed. Whether a mod is out of date is **policy, and policy belongs
to the base**.

So a strategy answers exactly two questions, both purely about its site: *which mod on this site is
this record?* and *what does the site currently say about these mods?* The base service does the rest
— joins observations back to tracked mods by key, normalizes, applies precedence, and emits the
result. A key the strategy returns no observation for becomes `Indeterminate("not found on page")`
without the strategy having to know that state exists.

This is what makes a later **bring-your-own-strategy** plausible: the surface a third party
implements is a parser and a key resolver, not a policy engine.

### The base/strategy split

| Base — written once, site-agnostic | Strategy — site-specific, and *only* this |
| --- | --- |
| Routing host → strategy; grouping tracked mods by site | Which URLs to fetch for a given set of keys |
| Fetching, user agent, timeouts, cancellation | Parsing a page into `SiteObservation`s |
| Version normalization and inequality comparison | Resolving hints → `SiteModKey` |
| Version-over-date precedence; the three-state outcome | Declaring `Capabilities` |
| Check-state persistence and last-checked bookkeeping | |
| Per-host politeness and concurrency | |
| Error containment (below) | |

**Two rules keep the split honest, and both are checkable in review:**

- **A site strategy touches no filesystem and no manifest.** It receives URLs and keys, returns data.
  This is the property that makes an untrusted third-party strategy safe to load, and it is a real
  constraint rather than a stylistic one — note that the existing `WickedWhimsUpdateStrategy` does
  the opposite (it holds `ModsManifestService` and `ModsFolderPathService` and writes to disk), which
  is fine for the mod-keyed spine but is exactly what a site strategy must never do.
- **A throwing or hanging strategy degrades to `Indeterminate` for its own mods and nothing else.**
  The base wraps every strategy call in a timeout and a catch. One bad site — or one bad third-party
  parser — must not fail the sweep for the other six.

Discovery is already solved by the pattern `ModUpdateOrchestrator` uses: inject
`IEnumerable<IModSiteStrategy>` and index it. An external assembly only needs to register into the
same container, so BYO becomes a loading question later rather than a redesign.

### Why the fetch is batch-shaped

`FetchObservationsAsync` takes every key for its site at once. A per-record interface would fetch
`downloads.html` once per installed Sacrificial mod — eight fetches for eight mods sharing one page.
Batching lets the strategy decide how many requests its site actually needs. It matters in the
opposite direction too: CurseForge and Nexus are one-page-per-mod, so their implementations turn the
batch into rate-limited concurrency — again a per-strategy decision the base does not need to know.

### The fetch seam

`IModPageFetcher` has only an HTTP implementation in v1; Sacrificial needs nothing more. It exists
now so Patreon/LoversLab/Nexus — which return a login wall or a challenge page to a cookieless client
and would be parsed as "no version found" — can later be served by a WebView-backed implementation
registered from the UI composition root, without reshaping the strategy interface. `SiteCapabilities`
is how a strategy asks for one, so the base can route the request rather than the strategy reaching
for a session itself.

## New types

One class per file, per repo convention.

| Type | Layer | Role |
| --- | --- | --- |
| `IModSiteStrategy` | Application | Site-keyed check contract (above) |
| `IModSiteUpdateService` | Application | Routes tracked records to strategies by host; the Updates page's single entry point |
| `IModPageFetcher` | Application | Page-fetch seam |
| `SiteModKey` | Application | Strategy-defined identity of a mod *within* a site |
| `SiteObservation` | Application | What a site currently says about one mod — the strategy's only output |
| `SiteCapabilities` | Application | Declares whether a strategy needs an authenticated fetch, supplies dates, etc. |
| `ModKeyHints` | Application | The inputs `TryResolveModKey` may use: page URL, download URL, display name, installed filenames |
| `UpdateTracking` | Application | The baseline block hung off `InstallRecord` |
| `TrackedMod` | Application | An install record paired with the display name `IModSiteUpdateService.CheckAsync` needs to build `ModKeyHints` — `InstallRecord` alone carries no display name |
| `SiteUpdateStatus` / `SiteUpdateCheckResult` | Application | `UpToDate` / `UpdateAvailable` / `Indeterminate` + observed values, one per checked mod |
| `UpdateCheckState` / `IUpdateCheckStateStore` | Application | Volatile per-record check state and its store |
| `SacrificialSiteStrategy` | Infrastructure | The one v1 strategy |
| `HttpModPageFetcher` | Infrastructure | `IModPageFetcher` over `HttpClient` |
| `UpdateCheckStateStore` | Infrastructure | JSON file under `%LOCALAPPDATA%` |

`UpdatesPageViewModel` (today a 9-line stub) grows into the real page.

## Changed types

| Type | Change |
| --- | --- |
| `InstallRecord` | New nullable `UpdateTracking? Tracking` |
| `IArchiveInstallService.InstallAsync` | New optional tracking argument, and the supersede argument (see below) |
| `ArchiveInstallService` | Writes tracking into the record; supersede handling; default display name from the site title |
| `ModsFileOperationsService` | Gains the stale-path prune (moved out of `WickedWhimsUpdateStrategy`, plus empty-directory cleanup) so both spines share it |
| `WickedWhimsUpdateStrategy` | `DeleteStaleFiles` delegates to the shared prune instead of owning it — behavior unchanged, including its containment guard |
| `ModsPageViewModel` | Install dialog gains a version field and an editable mod-page-URL field; supersede prompt on a mod-key match |
| `AddInfrastructureServices` | Registers the strategy, fetcher, and state store — **and `ServiceRegistrationTests` must be updated or it fails** |

## UI

Updates live on the **Updates page**, keyed by install record. This is not arbitrary: the Mods page
is per-*file*, and one install record covers many files. "Update available" cannot be a column on
that flat list without first answering how it renders across twelve rows that share a record. A
badge on the Mods page can follow once that has an answer.

The page lists tracked records with display name, installed version, site version, last checked, and
state. Actions: **Check for updates** (manual, v1's only trigger), **Open mod page** (hands off to
the Browse tab — this is what "detect-only" means in practice), and **Mark as current**.

**"Mark as current" is not optional.** It re-stamps the currently observed site values as the
baseline. Without it, any false positive — a typo'd version, a site that reformats its version
string, a mod the user updated outside the app — nags permanently with no user recourse, which is
how users end up turning the whole feature off.

### Adoption

Two fields, on both the install dialog and an edit-metadata path for already-installed mods: **paste
the mod page URL** and **type the version**. When the URL resolves and the page fetch succeeds, the
version field is *prefilled* from the same extractor the check will use, so both sides match by
construction and the user only types when the fetch fails or the site's string is wrong.

A failed resolution still stores the URL and marks the record unresolved, retried on the next check.
Nothing blocks on a live fetch — if the index cannot be parsed, checking for that site is broken
anyway, and adoption failing is that same outage rather than a new one.

## Record supersession — in scope for v1

`ArchiveInstallService` today does two individually-reasonable things that are jointly broken once
update checking exists:

- `ResolveModFolderName` ([ArchiveInstallService.cs:326](../../ModManager.Infrastructure/Services/ArchiveInstallService.cs))
  loops `while Directory.Exists` and appends `(2)`, `(3)` — it always mints a folder that does not
  yet exist.
- `PersistRecordAsync` does `Installs = [.. manifest.Installs, record]` — it always appends.

Walk the detect-only loop: install Zombie Apocalypse 2.3.1 (record A); the check reports 2.3.2; the
user downloads and installs it. Record B is appended, record A is untouched, and both file trees sit
in the Mods folder. Consequences:

1. **Both versions are fully installed** — a complete parallel copy, not stale leftovers. Sacrificial
   ships `.ts4script` files, so the game loads both and conflicts. This is exactly the corruption
   `WickedWhimsUpdateStrategy.DeleteStaleFiles` exists to prevent; that pruning lives only in the
   WickedWhims strategy and the general install path never received it.
2. **The Updates page lists the mod twice**, one row permanently claiming an update the user cannot
   clear. Each subsequent update adds another zombie row.
3. **The Mods page shows both trees.**

This is latent today — nothing currently tells a user to reinstall a mod they already have — and the
update badge is what activates it. Detect-only does not avoid the write side; it hands it to the user
instead of the strategy.

### Detection keys on the mod key, not the folder name

An earlier draft of this document proposed prompting on a folder-name collision, on the reasoning
that `ResolveModFolderName`'s dedup loop is already a collision detector. **The real install record
disproves that.** The install folder is `SAC_Zombie Apocalypse  -MOD- v2.3.1/`, because
`PreviewInstallAsync` defaults the display name to the archive's filename, which is version-stamped.
Version 2.3.2 produces `SAC_Zombie Apocalypse  -MOD- v2.3.2/` — a *different* folder name, so the
dedup loop never fires and there is no collision to prompt on.

Detection therefore keys on **`(SiteKey, SiteModKey)`** from the tracking URL's anchor, which is
version-stable by construction. Folder-name collision remains a weak secondary signal, useful only
for records with no tracking information at all.

| Option | Assessment |
| --- | --- |
| **Explicit** — the Updates page's action binds the next install to a specific record | Reliable, record known exactly. Misses the user who re-downloads without using the button. |
| **Implicit** — silently supersede on a matching mod key | Deletes files on a match the user never saw. Wrong direction for a destructive action. |
| **Prompted** — on a mod-key match, ask: "This looks like an update to Zombie Apocalypse (2.3.1). Update it, or install as new?" | Catches every path, never destroys without consent. |

**Decision: explicit + prompted.** Explicit binding when the user comes through the Updates page; the
prompt as the catch-all everywhere else. Never silent.

### The folder name must not encode the version

The install folder is `SAC_Zombie Apocalypse  -MOD- v2.3.1/` only because `PreviewInstallAsync`
defaults the display name to the archive's filename, which is version-stamped. Every downstream
problem follows from that one default: consecutive versions don't collide, an update would strand an
empty `...v2.3.1/` shell, and the mod's display name changes every release.

**Fix the cause, not the symptom.** For new installs, the default display name comes from the site
strategy's clean mod title (`Zombie Apocalypse`), so the folder is `Mods/Zombie Apocalypse/` and
**never needs renaming, for any version, ever.** A version-free folder name is the invariant worth
holding; leaving a stale one in place is only tolerable as a transitional state, not as a design.

For folders that are *already* version-stamped, the first supersede renames once — **after asking**.
The rename is the most destructive operation in this design and it runs on folders that predate the
feature, so it is a confirmed action ("Rename folder to *Zombie Apocalypse*?"), not a silent one, for
the same reason supersede itself is prompted. Declining keeps the old folder name and updates in
place; the prompt does not reappear for that record.

The steps, once confirmed:

1. `Directory.Move` the old folder to the clean title — **one filesystem operation**, not 45 file
   moves, and cheap within a volume.
2. Rewrite the path prefix everywhere it is stored: the install record's `Files`, the matching
   `ManifestFileEntry` rows, and any `ModGroup.Members` entries. This fan-out is the real cost of the
   rename, not the move itself — relative path is the app's identity for a file, so every store keyed
   on it has to move together.
3. Save the manifest **after** the move succeeds; if the save fails, move the folder back. A crash
   between the two would otherwise orphan every file's metadata — the same failure CLAUDE.md
   describes for renames made outside the app, except self-inflicted.

Two cases the rename has to handle: the clean title's folder already existing (fall back to the
existing dedup loop rather than failing the update), and a mod whose files are **split across both
roots** because individual files were disabled — the same folder name can exist under `Mods/` and
`Mods.Disabled/`, and both must be renamed or neither.

After the rename, supersede targets the previous record's install root and prunes the diff inside it.
`WickedWhimsUpdateStrategy.ResolveInstallRoot` already resolves that root, including the disabled-root
check, so the behavior is proven rather than novel. Subfolders that empty out still need removing, so
directory cleanup remains part of the prune.

### What supersession does

1. Replace record A with record B in `Installs`.
2. Extract into A's install root; delete paths in A absent from B, honoring the disabled root and the
   containment guard `DeleteStaleFiles` already carries; remove directories left empty.
3. Drop orphaned `ManifestFileEntry` rows for deleted paths.
4. **Carry forward user metadata** — display name, category, group membership, and the update
   baseline. Easy to miss, and losing a mod's category on every update is the kind of small betrayal
   that makes a feature feel unreliable.

Pruning returns `ModFileFailure` rather than throwing, per this repo's bulk-operation convention: a
file moved or deleted outside the app is skipped, not cause to abort the install. The logic moves out
of `WickedWhimsUpdateStrategy` into a place both callers can reach — `ModsFileOperationsService` is
the natural home, since "delete these relative paths from whichever root holds them" is a file
operation.

**Decision:** a superseded record is **removed** from `Installs` rather than kept with a
`SupersededBy` marker. The manifest is user-visible through the manifest viewer, and history nobody
reads is noise.

## Design decisions

| Decision | Reason |
| --- | --- |
| A second, site-keyed spine rather than reworking `IModUpdateStrategy` | The CLI depends on the mod-keyed path and has no reason to change. Two small interfaces beat one interface serving two dispatch models. |
| Dispatch on the tracking URL's host, not on `InstallSource.Provider` | `Provider` currently holds `"manual"`, `"browser"`, `"adopted"`, and `"wickedwhims"` — a mix of *how it arrived* and *who made it*, with no site anywhere. Deriving the site from the URL means no migration and no redefinition of an existing field. |
| Batch `CheckAsync` | One Sacrificial page covers every installed Sacrificial mod; per-record would refetch it once per mod. Cheap now, awkward to retrofit. |
| Inequality, not version ordering | Version schemes across the seven target sites are irreconcilable; a wrong comparer fails silently as "up to date". |
| Dates stored and compared as raw strings | Avoids MM-DD/DD-MM ambiguity and timezone entirely; change detection never needs the parsed value. |
| Baseline in the manifest, check state in `%LOCALAPPDATA%` | Different write frequencies. A background sweep should not rewrite a file inside a 40 GB Mods folder to record a timestamp. |
| Baseline separate from `InstallRecord.Version`, tracking URL separate from `InstallSource.ModPageUrl` | One pair is history, the other is what we compare and where we look. "Mark as current" and re-pointing a URL must not falsify provenance. |
| Explicit `Indeterminate` state | A silently broken scraper is indistinguishable from "up to date" without it. |
| Updates surfaced per install record on the Updates page | Records are the grain updates exist at; the Mods page is per-file. |
| `IModPageFetcher` seam with only an HTTP impl in v1 | Sacrificial needs nothing more, but Patreon/Nexus will need the authenticated WebView, and discovering that after the strategy interface is settled would force a reshape. |
| Detect-only in v1 | Asset selection is unsolvable on multi-download pages (a Patreon post with four variants), and `DownloadUrl` is frequently a signed one-shot link — as it already is for WickedWhims. |
| Supersession detects on `(SiteKey, SiteModKey)`, not folder-name collision | Sacrificial's install folders are version-stamped (`SAC_Zombie Apocalypse  -MOD- v2.3.1/`), so consecutive versions never collide and the existing dedup loop never fires. The anchor-derived key is version-stable. |
| Default display name from the site's mod title, not the archive filename | The archive filename carries the version. Fixing the default means new installs get a version-free folder that never needs renaming — every folder-churn problem downstream disappears rather than being managed. |
| Legacy version-stamped folders renamed once, on first supersede | A stale folder name is acceptable as a transitional state, not as a design. One `Directory.Move` plus a path-prefix rewrite is bounded work; leaving it forever is a permanent wart. |
| The legacy rename is user-confirmed, and declining is remembered | It is the most destructive step here and it runs on folders created before this feature existed. Same reasoning as the supersede prompt: never restructure a user's Mods folder on an inference they did not see. |
| Strategies return observations, never verdicts | Comparison policy stays in one place instead of being re-implemented (and drifting) per site. This is also what keeps a future bring-your-own-strategy to a parser rather than a policy engine. |
| Site strategies touch no filesystem and no manifest | Makes an untrusted third-party strategy safe to load, and keeps the seam narrow enough to test with an HTML fixture alone. |
| A failing strategy degrades to `Indeterminate` for its own mods only | One broken site — or one bad third-party parser — must not fail the sweep for the others. |
| Version never sniffed from installed filenames | Real records carry asset files with their own versions (`v1.0 Animations.package` beside `v2.3.1`), so a filename regex has multiple candidates and no way to choose. |

## Test strategy

Per repo convention — MSTest + Moq, `Method_WhenCondition_ThenOutcome`, mock at the use-case
boundary, real IO in a temp sandbox at the infrastructure boundary.

- `SacrificialSiteStrategy` against **saved HTML fixtures**, not the live site. Because the strategy
  returns observations and touches no disk, its whole test surface is "HTML in, `SiteObservation`s
  out" — no sandbox, no mocks beyond the fetcher. That narrowness is the point of the split, and it
  is the test shape every future strategy inherits.
- A redesigned-page fixture asserting the strategy returns an **empty observation list** rather than
  throwing — the strategy only ever reports what it found (or didn't); it is
  `ModSiteUpdateServiceTests` that asserts an absent key becomes `Indeterminate` rather than a false
  `UpToDate`, since that verdict is base-service policy, not the strategy's to render.
- A strategy that throws, and one that hangs: both degrade to `Indeterminate` for their own mods and
  leave other sites' results intact.
- `IModPageFetcher` mocked everywhere; no test touches the network.
- Comparison normalization: `2.6.3.2` vs `v2.6.3.2` vs `V2.6.3.2 ` all compare equal.
- Supersession against real IO in a sandbox: stale files deleted, disabled root honored, metadata
  carried forward, missing files skipped rather than throwing.
- The legacy folder rename: paths rewritten across record/manifest entries/group members, a failed
  manifest save rolls the move back, a name collision falls back to the dedup loop, and a mod split
  across both roots renames in both. Declining the prompt updates in place and does not ask again.
- `ServiceRegistrationTests` updated for the new registrations, or the DI-graph test fails.

## Implementation status

What exists today, against the plan above:

| Piece | Status | Where |
| --- | --- | --- |
| Shared stale-path prune, reused by WickedWhims and by supersede | Done | [ModsFileOperationsService.DeleteStalePathsAsync](../../ModManager.Infrastructure/Services/ModsFileOperationsService.cs) |
| `InstallAsync` supersede path (replace record, extract into existing root, prune, carry-forward metadata) | Done | [ArchiveInstallService.cs](../../ModManager.Infrastructure/Services/ArchiveInstallService.cs) |
| Legacy folder rename (unwired — see below) | Done, not yet triggered by any UI | [ModsFolderService.RenameInstallFolderAsync](../../ModManager.Infrastructure/Services/ModsFolderService.cs) |
| Base spine — `IModSiteStrategy`, `IModSiteUpdateService`, comparison policy, error containment, check-state persistence | Done, tested against a fake strategy | [ModSiteUpdateService.cs](../../ModManager.Application/Services/ModSiteUpdateService.cs) |
| `SacrificialSiteStrategy` + `HttpModPageFetcher` | Done, tested against real captured markup | [SacrificialSiteStrategy.cs](../../ModManager.Infrastructure/Services/Sacrificial/SacrificialSiteStrategy.cs) |
| Adoption sets tracking automatically from the pasted mod page URL | Done | [ModsFolderService.ResolveTracking](../../ModManager.Infrastructure/Services/ModsFolderService.cs) |
| `AdoptAsync` supersedes an overlapping prior record instead of duplicating it — re-adopting is the supported way to fix a typo'd URL/version | Done | [ModsFolderService.AdoptAsync](../../ModManager.Infrastructure/Services/ModsFolderService.cs) |
| `UpdateInstallTrackingAsync` (rewrite a record's baseline — backs both "mark as current" and adoption) | Done | [ModsFolderService.cs](../../ModManager.Infrastructure/Services/ModsFolderService.cs) |
| Updates page — list, manual check, open mod page, mark as current | Done | [UpdatesPageViewModel.cs](../../ModManager.Ui/ViewModels/UpdatesPageViewModel.cs) |
| Install-dialog version/URL fields for a *fresh* browser/file install | Not started — see below | — |
| Supersede prompt and legacy-folder-rename prompt wiring | Not started — see below | — |

Three things worth flagging precisely because they're easy to lose track of once code exists:

- **A fresh install doesn't get tracking yet — only adoption does.** `ArchiveInstallService.InstallAsync`
  (the path a browser download or "Install from file" takes) still calls with `version: null` and never
  touches `Tracking`. `ModsFolderService.AdoptAsync` — the path for a file *already on disk* — resolves
  it automatically from the pasted mod page URL. In practice this means: install a mod normally, then
  use Adopt (or re-adopt) to link it to its site; a first-class version/URL field on the install dialog
  itself is the natural next increment, and would want the same `ResolveTracking` logic, currently
  private to `ModsFolderService`, pulled somewhere `ArchiveInstallService` can reach too.
- **Supersede and the folder rename are real, tested capabilities with no caller.** `ModsPageViewModel`
  never invokes either — they were built as the filesystem-safety groundwork the install flow will need
  once it starts detecting "this looks like an update to a mod I already have," not because anything
  currently triggers them.
- **`TrackedMod` was added beyond this document's original type list.** `InstallRecord` carries no
  display name (that lives on the manifest's `ManifestFileEntry` rows), so `IModSiteUpdateService.CheckAsync`
  needed a small wrapper pairing a record with the display name needed to build `ModKeyHints`. See
  [TrackedMod.cs](../../ModManager.Application/Models/TrackedMod.cs).

## Out of scope

- Downloading or applying updates automatically (detect-only; see decisions table)
- Check on launch and background sweep — the manual button is v1's only trigger
- Any site other than Sacrificial; any Sacrificial page other than `downloads.html`
- A WebView-backed `IModPageFetcher` (seam only)
- Picking a mod from a fetched site index during adoption (paste-a-URL only)
- Per-host rate limiting and TTL caching — one request per check makes both moot for Sacrificial,
  and both become real at CurseForge/Nexus
- An update badge on the Mods page
- CLI access to site-based checking
