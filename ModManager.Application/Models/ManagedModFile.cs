namespace ModManager.Application.Models;

public sealed record ManagedModFile(string RelativePath, ModFileState State);
