namespace ModManager.Application.Models;

public sealed record ModFile(
    string RelativePath,
    ModFileState State,
    long SizeBytes,
    DateTime ModifiedUtc,
    bool IsConflicted = false,
    string? DisplayName = null,
    string? GroupId = null,
    string? InstallId = null,
    string? Version = null,
    DateTime? InstalledUtc = null,
    string? Provider = null,
    string? ModPageUrl = null,
    string? Category = null);
