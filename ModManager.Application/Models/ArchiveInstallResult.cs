namespace ModManager.Application.Models;

public sealed record ArchiveInstallResult<T>(bool Success, T? Value, string? Error)
{
    public static ArchiveInstallResult<T> Ok(T value) => new(true, value, null);

    public static ArchiveInstallResult<T> Fail(string error) => new(false, default, error);
}
