namespace ModManager.Application.Models;

public sealed record ModFileFailure(string RelativePath, string Reason);
