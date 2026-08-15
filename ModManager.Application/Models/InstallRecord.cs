namespace ModManager.Application.Models;

public sealed record InstallSource(string Provider, string? ModPageUrl, string? DownloadUrl);

public sealed record InstallRecordFile(string RelativePath, string Sha256, long SizeBytes);

public sealed record InstallRecord(
    string InstallId,
    InstallSource Source,
    string? Version,
    DateTime InstalledUtc,
    string? SourceArchivePath,
    IReadOnlyList<InstallRecordFile> Files,
    IReadOnlyList<string> SkippedEntries,
    UpdateTracking? Tracking = null);
