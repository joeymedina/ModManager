namespace ModManager.Application.Models;

public sealed record ModUpdateResult(
    string ModId,
    ModVersionInfo InstalledVersion,
    ModReleaseInfo LatestRelease,
    int VersionComparison,
    bool DownloadPerformed,
    int InstalledFileCount)
{
    public bool IsUpdateAvailable => VersionComparison < 0;
}
