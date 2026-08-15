# Adding a site strategy — playbook

This is a **process doc**, not a feature doc — a repeatable checklist for adding the next
`IModSiteStrategy` (ModTheSims, CurseForge, Patreon, NexusMods, TheSimsResource, …) on top of the
site-based update-checking spine built for Sacrificial. Read
[mod-update-sites.md](./mod-update-sites.md) first for the *design*; this doc is the *procedure*.

Written so a new chat with zero memory of the Sacrificial work can pick this up cold — everything it
needs to know is either in this file, in `mod-update-sites.md`, or discoverable by reading the
Sacrificial implementation this doc keeps pointing at.

## Quick-start prompt for a new chat

```
I'm adding a new IModSiteStrategy for <SITE NAME> (<domain>) to ModManager. Read
docs/architecture/adding-a-site-strategy.md and docs/architecture/mod-update-sites.md first — the
first is the checklist to follow, the second is the design those pieces implement. Then read
ModManager.Infrastructure/Services/Sacrificial/SacrificialSiteStrategy.cs and its tests in
ModManager.Tests/Infrastructure/Services/Sacrificial/ as the reference implementation to mirror.

Start with research: fetch <site>'s actual page via curl (not WebFetch, which only returns
AI-summarized markdown, not real markup) and inspect the raw HTML before writing any parser code.
Don't assume the site's shape (one page listing every mod vs. one page per mod, auth requirements,
version/date format) — go look.
```

## What's already built — don't redo this

The base spine is done, tested, and should not need to change for a new site. If you find yourself
editing anything outside `ModManager.Infrastructure/Services/<NewSiteName>/` and its test
counterpart, stop and reconsider — the one known exception is a new `IModPageFetcher` implementation
for an auth-requiring site (see "The auth gap" below).

| Piece | What it does | File |
| --- | --- | --- |
| `IModSiteStrategy` | The interface a new site implements — `SiteKey`, `Hosts`, `Capabilities`, `TryResolveModKey`, `FetchObservationsAsync` | [IModSiteStrategy.cs](../../ModManager.Application/Interfaces/IModSiteStrategy.cs) |
| `IModPageFetcher` | What a strategy fetches through — `HttpModPageFetcher` covers no-auth sites | [IModPageFetcher.cs](../../ModManager.Application/Interfaces/IModPageFetcher.cs), [HttpModPageFetcher.cs](../../ModManager.Infrastructure/Services/HttpModPageFetcher.cs) |
| `ModSiteUpdateService` | Owns everything a strategy must not: routing by site, resolving unresolved keys, version/date comparison policy, the three-state outcome, per-strategy error containment, check-state persistence | [ModSiteUpdateService.cs](../../ModManager.Application/Services/ModSiteUpdateService.cs) |
| `SiteTrackingResolver` | Shared host→strategy matching + mod-key resolution, used by adoption, fresh installs, and supersede detection | [SiteTrackingResolver.cs](../../ModManager.Infrastructure/Services/SiteTrackingResolver.cs) |
| Updates page | Lists tracked installs, runs checks, shows results, "mark as current" | [UpdatesPageViewModel.cs](../../ModManager.Ui/ViewModels/UpdatesPageViewModel.cs) |
| Install/adopt dialogs | Editable version + mod-page-URL fields feeding `Tracking` resolution | [InstallDialogContent.axaml](../../ModManager.Ui/Views/Dialogs/InstallDialogContent.axaml), [AdoptDialogContent.axaml](../../ModManager.Ui/Views/Dialogs/AdoptDialogContent.axaml) |
| Supersede detection + prompt | `(SiteKey, SiteModKey)` collision check, confirm-before-overwrite, legacy-folder-rename offer | [ModsFolderService.FindMatchingTrackedInstallAsync](../../ModManager.Infrastructure/Services/ModsFolderService.cs), [ModsPageViewModel.ConfirmInstallAsync](../../ModManager.Ui/ViewModels/ModsPageViewModel.cs) |

A new site strategy plugs into every one of these for free, purely through DI registration — none of
them know Sacrificial exists by name.

## What's genuinely new per site

Everything else is real work, and most of it is research, not code:

1. **Fetch the real page.** `curl -s -A "Mozilla/5.0" <url> -o page.html`, not `WebFetch` (which
   summarizes through a model and never gives you literal markup — this was the single highest-value
   step in the Sacrificial build; it caught a two-download-link-per-entry structure and a title
   attribute that outright disagreed with the visible heading, neither of which a text summary would
   have surfaced).
2. **Determine the site's shape.** One page listing every mod (Sacrificial) vs. one page per mod
   (most others, probably). This decides what `FetchObservationsAsync` batches: one fetch for
   everything, or one fetch per key at whatever concurrency the strategy chooses. Don't assume —
   Sacrificial's shape is the easy, unusual case.
3. **Determine auth requirements.** Does a cookieless request return the real content, or a login
   wall / Cloudflare challenge? Sets `SiteCapabilities.RequiresAuthenticatedSession`. See "The auth
   gap" below if the answer is yes.
4. **Find the mod's stable identity signal.** Sacrificial's is a URL fragment matching the site's own
   "copy link" permalink — use it unmodified, no transformation, because it already *is* the site's
   canonical identity. A one-page-per-mod site's identity is probably just its URL path — simpler.
   Whatever it is, `TryResolveModKey` must be able to derive it from `ModKeyHints` (page URL, download
   URL, display name, installed paths) without fetching anything — resolution is synchronous by
   design, so it must be local computation only.
5. **Find version and update-date signals**, and their format quirks. Expect irregularities — don't
   assume zero-padded dates, clean version strings, or single download links. Sacrificial had
   unpadded days (`09-7-2025`), a version badge that disagreed with the download filename's casing,
   and two download links per card (direct + Patreon alternate) where picking the wrong one silently
   works but tracks the wrong source.
6. **Build fixtures from the real captured HTML**, trimmed to a representative slice — not an
   idealized guess at the structure. See "Fixtures" below.
7. **Implement the strategy.** New folder, `ModManager.Infrastructure/Services/<SiteName>/`, one
   class. Regex-based parsing matches this codebase's existing convention (`WickedWhimsReleaseClient`
   does the same, no HTML-parsing library dependency exists) — don't introduce one for a single site
   unless the markup genuinely can't be handled with anchored regexes.
8. **Register in DI** — one line in `AddInfrastructureServices` (`services.AddSingleton<IModSiteStrategy, NewSiteStrategy>();`).
9. **Test against the fixtures.** No network calls in any test, ever.

## The auth gap

`IModPageFetcher` has exactly one implementation today — `HttpModPageFetcher`, session-less. Any site
that returns a login wall or a Cloudflare-style challenge to a cookieless request (Patreon almost
certainly; possibly NexusMods, LoversLab-adjacent sites) needs a **WebView-backed** implementation
that doesn't exist yet — it was left as a seam (`SiteCapabilities.RequiresAuthenticatedSession`)
specifically so this could be built once real need forced the shape, rather than guessed at now.
Building that fetcher is its own chunk of work, separate from writing the strategy itself: it likely
means reusing the Browse page's `IBrowsePageBrowser`/`WebView` machinery, registered from the UI
composition root (`ModManager.Ui`) rather than `ModManager.Infrastructure`, since only the UI layer
has a live WebView. If the next site you're adding needs this, say so up front rather than
discovering it mid-implementation — it changes where some of the work has to happen.

## Fixtures

Reference: [SacrificialFixtures.cs](../../ModManager.Tests/Infrastructure/Services/Sacrificial/SacrificialFixtures.cs).

- Embed as a C# raw string literal (`"""..."""`) directly in a `Fixtures.cs` file next to the
  strategy's tests — no build-time copy-to-output-directory mechanism exists in this repo, and a raw
  string keeps the fixture co-located and diffable.
- Use a **real, multi-entry excerpt**, including whatever non-entry content sits between entries on
  the actual page (ads, comments, category dividers for Sacrificial) — this is what proves the parser
  finds entry boundaries correctly rather than just parsing one isolated block.
- Add a **redesigned-page fixture** — content with none of the expected markup — and assert the
  parser returns an **empty list**, not that it throws or returns garbage. The three-state
  `Indeterminate` outcome downstream depends on strategies failing this way. This is the one test that
  most directly protects against a live site silently breaking the feature later.
- Note in a comment when the fixture was captured (`captured 2026-08-13`) — it will go stale, and
  future you needs to know when to recapture rather than debug a fixture that no longer matches
  reality.

## Design rules a new strategy must not violate

These are enforced by the type system in some places and only by convention in others — worth
restating because convention is easy to drift from under time pressure:

- **A strategy returns observations, never a verdict.** No `UpToDate`/`UpdateAvailable` logic inside
  a strategy — that's `ModSiteUpdateService`'s job. A strategy answers "what does the site say" and
  "which mod is this," nothing else.
- **A strategy touches no filesystem and no manifest.** It receives URLs and keys, returns data. This
  is what makes a third-party strategy safe to load someday.
- **Only track when a strategy's `Hosts` actually matches.** `SiteTrackingResolver.ResolveTracking`
  and `.TryMatchStrategy` both return null on no match — never invent a site key from a raw host for
  an unmatched URL. An untracked mod is honest; a tracked-but-permanently-`Indeterminate` mod is
  Updates-page noise for a site nobody asked to check.
- **Dates are opaque strings, compared for inequality, never parsed.** Versions are normalized
  (trim, strip a leading `v`/`V`, case-insensitive) and also compared for inequality, never ordered,
  unless a specific site's scheme is confirmed well-behaved enough to justify an exception — document
  that exception loudly if you add one.
- **`ParseXxx`-style static parsing methods should be `public`, not `internal`.** This repo's test
  project has no `InternalsVisibleTo` grant from `ModManager.Infrastructure` — confirmed the hard way
  once already. A pure, stateless parser has no real encapsulation to protect by staying internal.

## Known friction points from the first pass

- **Adding a required constructor parameter to a shared Infrastructure service breaks every test call
  site that constructs it directly.** `grep -rn "new <ServiceName>(" --include="*.cs"` across the
  whole repo before making the change, so you know the full blast radius up front rather than
  discovering it one compile error at a time.
- **`dotnet build` catches most AXAML binding mistakes** (compiled bindings validate property paths
  against `x:DataType`), but not everything — a bad `DynamicResource` key or an invalid
  `FASymbolIcon` `Symbol` value only fails at runtime. Stick to binding patterns and symbol names
  already proven elsewhere in this codebase (`grep` for them) rather than guessing at new ones,
  especially if you're working without a Windows machine to actually render the result.
- **Fire-and-forget async view-model methods are hard to test.** If a flow needs testing end-to-end
  and the entry point discards its `Task` (`_ = SomeAsync()`), consider returning the `Task` instead —
  it's a non-breaking signature change if the only caller already doesn't await it, and it's what
  makes the flow testable at all.

## Definition of done, per site

Mirrors what shipped for Sacrificial:

- [ ] Real page fetched and inspected via curl, not summarized
- [ ] `SiteCapabilities.RequiresAuthenticatedSession` decided (and the auth-fetcher gap flagged if true)
- [ ] Strategy implemented: `SiteKey`, `Hosts`, `TryResolveModKey`, `FetchObservationsAsync`
- [ ] Fixtures built from real captured markup, including a redesigned-page/empty-result case
- [ ] Unit tests against fixtures only — no network calls
- [ ] Registered in `AddInfrastructureServices`
- [ ] `ServiceRegistrationTests` still passes (confirms the DI graph resolves)
- [ ] Full suite run (`dotnet test ModManager.slnx`) — expect the same 2 pre-existing macOS-only
      failures (`BrowserTabViewModelTests`, `ModFileViewModelTests`) and nothing else
- [ ] [mod-update-sites.md](./mod-update-sites.md)'s "The site" section gets a sibling section for
      the new site, with whatever real-markup findings and format quirks came out of step 1
