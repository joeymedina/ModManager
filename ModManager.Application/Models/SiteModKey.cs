namespace ModManager.Application.Models;

/// <summary>
/// A strategy-defined identity for a mod within its own site — Sacrificial derives this from a mod
/// page URL's anchor fragment, other sites may use a numeric id or a URL slug. Wrapped rather than a
/// bare string so a raw URL or display name can't be passed where a resolved key is expected.
/// </summary>
public sealed record SiteModKey(string Value);
