namespace ModManager.Application.Models;

/// <summary>
/// An install record paired with the display name a check needs to build <see cref="ModKeyHints"/>.
/// <see cref="InstallRecord"/> itself carries no display name — that lives on the manifest's
/// <see cref="ManifestFileEntry"/> rows — so the caller, which has already loaded the manifest,
/// supplies it here rather than <see cref="IModSiteUpdateService"/> reaching for the manifest itself.
/// </summary>
public sealed record TrackedMod(InstallRecord Record, string DisplayName);
