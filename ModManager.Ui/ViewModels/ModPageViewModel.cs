using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public partial class ModPageViewModel : ViewModelBase
{
    private readonly IModsFolderUseCase _modsFolderUseCase;

    public ObservableCollection<ManagedModViewModel> Mods { get; } = [];

    [ObservableProperty]
    private string _modsFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mods");

    [ObservableProperty]
    private string _disabledModsFolderPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Enter a mods folder path and click Refresh.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private ManagedModViewModel? _selectedMod;

    public ModPageViewModel()
        : this(new DesignTimeModsFolderUseCase())
    {
    }

    public ModPageViewModel(IModsFolderUseCase modsFolderUseCase)
    {
        _modsFolderUseCase = modsFolderUseCase;
        UpdateLayoutPaths();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ModsFolderPath))
        {
            StatusMessage = "Mods folder path is required.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading mods...";

        try
        {
            UpdateLayoutPaths();
            IReadOnlyList<ManagedMod> mods = await _modsFolderUseCase.LoadModsAsync(ModsFolderPath.Trim());
            ReplaceMods(mods);
            StatusMessage = mods.Count == 0
                ? "No mods found."
                : $"Loaded {mods.Count} mod(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load mods: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task EnableModAsync(ManagedModViewModel mod)
    {
        await RunModActionAsync(mod, "Enabling", async () =>
        {
            ManagedMod updated = await _modsFolderUseCase.EnableModAsync(ModsFolderPath.Trim(), mod.ModId);
            mod.Apply(updated);
            StatusMessage = $"Enabled {mod.Name}.";
        });
    }

    public async Task DisableModAsync(ManagedModViewModel mod)
    {
        await RunModActionAsync(mod, "Disabling", async () =>
        {
            ManagedMod updated = await _modsFolderUseCase.DisableModAsync(ModsFolderPath.Trim(), mod.ModId);
            mod.Apply(updated);
            StatusMessage = $"Disabled {mod.Name}.";
        });
    }

    public async Task DeleteModAsync(ManagedModViewModel mod)
    {
        await RunModActionAsync(mod, "Deleting", async () =>
        {
            await _modsFolderUseCase.DeleteModAsync(ModsFolderPath.Trim(), mod.ModId);
            Mods.Remove(mod);
            if (ReferenceEquals(SelectedMod, mod))
            {
                SelectedMod = null;
            }

            StatusMessage = $"Deleted {mod.Name}.";
        });
    }

    private async Task RunModActionAsync(ManagedModViewModel mod, string actionLabel, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(mod);
        ArgumentNullException.ThrowIfNull(action);

        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ModsFolderPath))
        {
            StatusMessage = "Mods folder path is required.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"{actionLabel} {mod.Name}...";

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed while {actionLabel.ToLowerInvariant()} {mod.Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReplaceMods(IReadOnlyList<ManagedMod> mods)
    {
        string? selectedId = SelectedMod?.ModId;
        Mods.Clear();

        foreach (ManagedMod mod in mods)
        {
            Mods.Add(new ManagedModViewModel(this, mod));
        }

        SelectedMod = selectedId is null
            ? null
            : Mods.FirstOrDefault(item => string.Equals(item.ModId, selectedId, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateLayoutPaths()
    {
        if (string.IsNullOrWhiteSpace(ModsFolderPath))
        {
            DisabledModsFolderPath = string.Empty;
            return;
        }

        try
        {
            ModsFolderLayout layout = _modsFolderUseCase.GetLayout(ModsFolderPath.Trim());
            ModsFolderPath = layout.ModsFolderPath;
            DisabledModsFolderPath = layout.DisabledModsFolderPath;
        }
        catch
        {
            DisabledModsFolderPath = string.Empty;
        }
    }

    private sealed class DesignTimeModsFolderUseCase : IModsFolderUseCase
    {
        public ModsFolderLayout GetLayout(string modsFolderPath)
        {
            string path = string.IsNullOrWhiteSpace(modsFolderPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mods")
                : Path.GetFullPath(modsFolderPath);

            string parent = Path.GetDirectoryName(path) ?? path;
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return new ModsFolderLayout(path, Path.Combine(parent, $"{name}.Disabled"));
        }

        public Task<IReadOnlyList<ManagedMod>> LoadModsAsync(string modsFolderPath, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ManagedMod> mods =
            [
                new ManagedMod("demo-1", "WickedWhims", "WickedWhims", [new ManagedModFile("WickedWhims_main.package", ModFileState.Enabled)], false),
                new ManagedMod("demo-2", "ExtremeViolence", "ExtremeViolence", [new ManagedModFile("ExtremeViolence.package", ModFileState.Disabled)], false)
            ];
            return Task.FromResult(mods);
        }

        public Task<ManagedMod> EnableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ManagedMod(modId, modId, modId, [new ManagedModFile($"{modId}.package", ModFileState.Enabled)], false));

        public Task<ManagedMod> DisableModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ManagedMod(modId, modId, modId, [new ManagedModFile($"{modId}.package", ModFileState.Disabled)], false));

        public Task DeleteModAsync(string modsFolderPath, string modId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
