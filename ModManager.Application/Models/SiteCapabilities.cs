namespace ModManager.Application.Models;

/// <summary>
/// What a site strategy needs in order to be checked, declared rather than assumed so the base
/// service can route the fetch correctly instead of the strategy reaching for a session itself.
/// </summary>
/// <param name="RequiresAuthenticatedSession">
/// True for sites that return a login wall or a challenge page to a cookieless client (Patreon,
/// LoversLab, Nexus) — routes the fetch through a session-aware <see cref="IModPageFetcher"/>
/// instead of the default HTTP one.
/// </param>
/// <param name="ProvidesUpdatedOnDate">
/// False for sites with no reliable per-mod updated date, so the base service knows not to fall back
/// to date comparison for a strategy that can't supply one.
/// </param>
public sealed record SiteCapabilities(bool RequiresAuthenticatedSession = false, bool ProvidesUpdatedOnDate = true);
