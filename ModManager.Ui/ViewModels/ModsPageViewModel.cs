using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public partial class ModsPageViewModel : ViewModelBase
{
    private readonly IModsFolderUseCase _modsFolderUseCase;
    private List<ModFileViewModel> _allFiles = [];

    public ObservableCollection<ModFileViewModel> Files { get; } = [];

    public ObservableCollection<ModFileViewModel> SelectedFiles { get; } = [];

    [ObservableProperty]
    private string _modsFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Mods");

    [ObservableProperty]
    private string _disabledModsFolderPath = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Enter a mods folder path and click Refresh.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _detailHeader = "Select a file";

    [ObservableProperty]
    private string _detailBody = string.Empty;

    [ObservableProperty]
    private bool _isDeleteConfirmationVisible;

    [ObservableProperty]
    private string _deleteConfirmationMessage = string.Empty;

    public ModsPageViewModel()
        : this(new DesignTimeModsFolderUseCase())
    {
    }

    public ModsPageViewModel(IModsFolderUseCase modsFolderUseCase)
    {
        _modsFolderUseCase = modsFolderUseCase;
        SelectedFiles.CollectionChanged += (_, _) => UpdateDetails();
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
            await LoadFilesCoreAsync();
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

    [RelayCommand]
    private Task EnableSelectedAsync() => RunBulkActionAsync(
        "Enabling", "Enabled",
        (root, paths, ct) => _modsFolderUseCase.EnableAsync(root, paths, ct));

    [RelayCommand]
    private Task DisableSelectedAsync() => RunBulkActionAsync(
        "Disabling", "Disabled",
        (root, paths, ct) => _modsFolderUseCase.DisableAsync(root, paths, ct));

    [RelayCommand]
    private void RequestDeleteSelected()
    {
        if (SelectedFiles.Count == 0)
        {
            StatusMessage = "Select one or more files first.";
            return;
        }

        DeleteConfirmationMessage = $"Delete {SelectedFiles.Count} file(s) permanently? This cannot be undone.";
        IsDeleteConfirmationVisible = true;
    }

    [RelayCommand]
    private void CancelDeleteSelected()
    {
        IsDeleteConfirmationVisible = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSelectedAsync()
    {
        IsDeleteConfirmationVisible = false;
        await RunBulkActionAsync(
            "Deleting", "Deleted",
            (root, paths, ct) => _modsFolderUseCase.DeleteAsync(root, paths, ct));
    }

    private async Task RunBulkActionAsync(
        string progressLabel,
        string resultLabel,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ModFileFailure>>> action)
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

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        if (paths.Count == 0)
        {
            StatusMessage = "Select one or more files first.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"{progressLabel} {paths.Count} file(s)...";

        try
        {
            IReadOnlyList<ModFileFailure> failures = await action(ModsFolderPath.Trim(), paths, CancellationToken.None);
            await LoadFilesCoreAsync();
            StatusMessage = BuildResultMessage(resultLabel, paths.Count, failures);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed while {progressLabel.ToLowerInvariant()}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildResultMessage(string resultLabel, int total, IReadOnlyList<ModFileFailure> failures)
    {
        int succeeded = total - failures.Count;
        if (failures.Count == 0)
        {
            return $"{resultLabel} {succeeded} file(s).";
        }

        string reasons = string.Join("; ", failures.Select(failure => $"{failure.RelativePath}: {failure.Reason}"));
        return $"{resultLabel} {succeeded} file(s), {failures.Count} failed: {reasons}";
    }

    private async Task LoadFilesCoreAsync()
    {
        UpdateLayoutPaths();
        IReadOnlyList<ModFile> files = await _modsFolderUseCase.LoadFilesAsync(ModsFolderPath.Trim());
        ReplaceFiles(files);
        StatusMessage = files.Count == 0
            ? "No mod files found."
            : $"Loaded {files.Count} file(s).";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Files.Clear();

        IEnumerable<ModFileViewModel> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allFiles
            : _allFiles.Where(file => file.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (ModFileViewModel file in filtered)
        {
            Files.Add(file);
        }
    }

    private void ReplaceFiles(IReadOnlyList<ModFile> files)
    {
        SelectedFiles.Clear();
        _allFiles = [.. files.Select(file => new ModFileViewModel(file))];
        ApplyFilter();
    }

    private void UpdateDetails()
    {
        if (SelectedFiles.Count == 0)
        {
            DetailHeader = "Select a file";
            DetailBody = string.Empty;
        }
        else if (SelectedFiles.Count == 1)
        {
            ModFileViewModel file = SelectedFiles[0];
            DetailHeader = file.Name;
            DetailBody = $"Folder: {(string.IsNullOrEmpty(file.Folder) ? "(root)" : file.Folder)}\n"
                + $"Size: {file.SizeBytes:N0} bytes\n"
                + $"Modified: {file.ModifiedUtc:u}\n"
                + $"Status: {file.StatusText}";
        }
        else
        {
            long totalBytes = SelectedFiles.Sum(file => file.SizeBytes);
            DetailHeader = $"{SelectedFiles.Count} files selected";
            DetailBody = $"Total size: {totalBytes:N0} bytes";
        }
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

        public Task<IReadOnlyList<ModFile>> LoadFilesAsync(string modsFolderPath, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ModFile> files =
            [
                new ModFile("WickedWhims_main.package", ModFileState.Enabled, 1_048_576, DateTime.UtcNow),
                new ModFile("Extras/ExtremeViolence.package", ModFileState.Disabled, 524_288, DateTime.UtcNow)
            ];
            return Task.FromResult(files);
        }

        public Task<IReadOnlyList<ModFileFailure>> EnableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModFileFailure>>([]);

        public Task<IReadOnlyList<ModFileFailure>> DisableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModFileFailure>>([]);

        public Task<IReadOnlyList<ModFileFailure>> DeleteAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModFileFailure>>([]);
    }
}
