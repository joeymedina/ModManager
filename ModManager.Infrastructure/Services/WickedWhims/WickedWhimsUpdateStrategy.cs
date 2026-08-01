using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Infrastructure.Services.WickedWhims;

internal sealed class WickedWhimsUpdateStrategy(
    WickedWhimsVersionDetector versionDetector,
    WickedWhimsReleaseClient releaseClient,
    WickedWhimsArchiveInstaller archiveInstaller) : IModUpdateStrategy
{
    public const string StrategyModId = "wickedwhims";

    public string ModId => StrategyModId;

    /// <summary>
    /// Executes WickedWhims version check and optional download/install.
    /// </summary>
    public async Task<ModUpdateResult> ExecuteAsync(ModUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        InstalledModVersion installedVersion = versionDetector.FindInstalledVersion(request.ModsFolder)
            ?? throw new InvalidOperationException($"No WickedWhims version found in {request.ModsFolder}.");

        ModReleaseInfo latestRelease = await releaseClient.GetLatestReleaseAsync(cancellationToken);

        int comparison = WickedWhimsVersionDetector.CompareVersions(installedVersion.Version, latestRelease.Version)
            ?? throw new InvalidOperationException("Could not compare installed and latest versions.");

        bool downloadPerformed = false;
        int installedFileCount = 0;

        if (comparison < 0 && request.DownloadIfUpdateAvailable)
        {
            byte[] archiveBytes = await releaseClient.DownloadLatestArchiveAsync(cancellationToken);
            installedFileCount = archiveInstaller.InstallArchive(request.ModsFolder, archiveBytes);
            downloadPerformed = true;
        }

        return new ModUpdateResult(
            request.ModId,
            new ModVersionInfo(installedVersion.Version, installedVersion.Source),
            latestRelease,
            comparison,
            downloadPerformed,
            installedFileCount);
    }
}
