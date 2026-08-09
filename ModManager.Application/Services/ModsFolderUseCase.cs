using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Application.Services;

public sealed class ModsFolderUseCase(IModsFolderRepository repository) : IModsFolderUseCase
{
    /// <summary>
    /// Resolves active and disabled mods folder paths.
    /// </summary>
    public ModsFolderLayout GetLayout(string modsFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.GetLayout(modsFolderPath);
    }

    /// <summary>
    /// Discovers mod files from both active and disabled folders. Never writes.
    /// </summary>
    public Task<IReadOnlyList<ModFile>> LoadFilesAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        return repository.LoadFilesAsync(modsFolderPath, cancellationToken);
    }

    /// <summary>
    /// Moves the given files into the active mods folder. Continues past per-file failures.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> EnableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.EnableAsync(modsFolderPath, relativePaths, cancellationToken);
    }

    /// <summary>
    /// Moves the given files into the disabled mods folder. Continues past per-file failures.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> DisableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.DisableAsync(modsFolderPath, relativePaths, cancellationToken);
    }

    /// <summary>
    /// Deletes the given files from active and/or disabled folders. Continues past per-file failures.
    /// </summary>
    public Task<IReadOnlyList<ModFileFailure>> DeleteAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsFolderPath);
        ArgumentNullException.ThrowIfNull(relativePaths);
        return repository.DeleteAsync(modsFolderPath, relativePaths, cancellationToken);
    }
}
