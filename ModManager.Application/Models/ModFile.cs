namespace ModManager.Application.Models;

public sealed record ModFile(
    string RelativePath,
    ModFileState State,
    long SizeBytes,
    DateTime ModifiedUtc,
    bool IsConflicted = false);
