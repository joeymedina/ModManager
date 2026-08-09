using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public enum ModsListMode
{
    Flat,
    Folder,
    Group,
}

public partial class ModsPageViewModel : ViewModelBase
{
    private readonly IModsFolderUseCase _modsFolderUseCase;
    private readonly IArchiveInstallService _archiveInstallService;
    private List<ModFileViewModel> _allFiles = [];
    private List<ModGroup> _groups = [];
    private Uri? _pendingInstallSourceUri;
    private Uri? _pendingInstallModPageUri;

    public ObservableCollection<ModFileViewModel> Files { get; } = [];

    public ObservableCollection<ModFileViewModel> SelectedFiles { get; } = [];

    public ObservableCollection<ModTreeNodeViewModel> FolderTree { get; } = [];

    public ObservableCollection<ModTreeNodeViewModel> SelectedTreeNodes { get; } = [];

    public ObservableCollection<ModGroupNodeViewModel> GroupTree { get; } = [];

    public ObservableCollection<ModGroupNodeViewModel> SelectedGroupNodes { get; } = [];

    [ObservableProperty]
    private ModsListMode _listMode = ModsListMode.Flat;

    [ObservableProperty]
    private string _modsFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Electronic Arts\\The Sims 4\\Mods");

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

    public ObservableCollection<ArchiveEntryPreviewViewModel> ArchivePreviewEntries { get; } = [];

    [ObservableProperty]
    private bool _isInstallPanelVisible;

    [ObservableProperty]
    private string _archivePathToInstall = string.Empty;

    [ObservableProperty]
    private string _installDisplayName = string.Empty;

    [ObservableProperty]
    private bool _hasArchivePreview;

    [ObservableProperty]
    private string _installStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isAdoptPanelVisible;

    [ObservableProperty]
    private string _adoptDisplayName = string.Empty;

    [ObservableProperty]
    private string _adoptVersion = string.Empty;

    [ObservableProperty]
    private string _adoptModPageUrl = string.Empty;

    [ObservableProperty]
    private string _adoptStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isAddToGroupPanelVisible;

    [ObservableProperty]
    private string _groupNameInput = string.Empty;

    [ObservableProperty]
    private string _addToGroupStatusMessage = string.Empty;

    public ObservableCollection<string> ExistingGroupNames { get; } = [];

    public ModsPageViewModel()
        : this(new DesignTimeModsFolderUseCase(), new DesignTimeArchiveInstallService())
    {
    }

    public ModsPageViewModel(IModsFolderUseCase modsFolderUseCase, IArchiveInstallService archiveInstallService)
    {
        _modsFolderUseCase = modsFolderUseCase;
        _archiveInstallService = archiveInstallService;
        SelectedFiles.CollectionChanged += (_, _) => UpdateDetails();
        SelectedTreeNodes.CollectionChanged += (_, _) => SyncSelectedFilesFromTree();
        SelectedGroupNodes.CollectionChanged += (_, _) => SyncSelectedFilesFromGroupTree();
        UpdateLayoutPaths();
    }

    /// <summary>
    /// Opens the install panel pre-filled with a downloaded archive's path and runs its preview,
    /// so the browser's "Install to Mods" prompt lands the user straight at the selection screen.
    /// </summary>
    public void BeginInstallFromFile(string archivePath, Uri? sourceUri = null, Uri? modPageUri = null)
    {
        ArchivePathToInstall = archivePath;
        _pendingInstallSourceUri = sourceUri;
        _pendingInstallModPageUri = modPageUri;
        IsInstallPanelVisible = true;
        _ = PreviewInstallAsync();
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

    [RelayCommand]
    private void SetListMode(ModsListMode mode) => ListMode = mode;

    [RelayCommand]
    private void RequestAdoptSelected()
    {
        if (SelectedFiles.Count == 0)
        {
            StatusMessage = "Select one or more files first.";
            return;
        }

        IsAdoptPanelVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmAdoptAsync()
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

        if (string.IsNullOrWhiteSpace(AdoptDisplayName))
        {
            AdoptStatusMessage = "Enter a name for this mod.";
            return;
        }

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        if (paths.Count == 0)
        {
            AdoptStatusMessage = "Select one or more files first.";
            return;
        }

        IsBusy = true;
        AdoptStatusMessage = "Adopting...";

        try
        {
            string? modPageUrl = string.IsNullOrWhiteSpace(AdoptModPageUrl) ? null : AdoptModPageUrl.Trim();
            string? version = string.IsNullOrWhiteSpace(AdoptVersion) ? null : AdoptVersion.Trim();

            ArchiveInstallResult<InstallRecord> result = await _modsFolderUseCase.AdoptAsync(
                ModsFolderPath.Trim(),
                paths,
                AdoptDisplayName.Trim(),
                modPageUrl,
                version);

            if (!result.Success)
            {
                AdoptStatusMessage = result.Error ?? "Adopt failed.";
                return;
            }

            string adoptedDisplayName = AdoptDisplayName;
            IsAdoptPanelVisible = false;
            ResetAdoptPanel();
            await LoadFilesCoreAsync();
            StatusMessage = $"Adopted {paths.Count} file(s) as \"{adoptedDisplayName}\".";
        }
        catch (Exception ex)
        {
            AdoptStatusMessage = $"Adopt failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelAdopt()
    {
        IsAdoptPanelVisible = false;
        ResetAdoptPanel();
    }

    private void ResetAdoptPanel()
    {
        AdoptDisplayName = string.Empty;
        AdoptVersion = string.Empty;
        AdoptModPageUrl = string.Empty;
        AdoptStatusMessage = string.Empty;
    }

    [RelayCommand]
    private void RequestAddToGroup()
    {
        if (SelectedFiles.Count == 0)
        {
            StatusMessage = "Select one or more files first.";
            return;
        }

        IsAddToGroupPanelVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmAddToGroupAsync()
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

        if (string.IsNullOrWhiteSpace(GroupNameInput))
        {
            AddToGroupStatusMessage = "Enter a group name.";
            return;
        }

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        if (paths.Count == 0)
        {
            AddToGroupStatusMessage = "Select one or more files first.";
            return;
        }

        IsBusy = true;
        AddToGroupStatusMessage = "Adding...";

        try
        {
            ArchiveInstallResult<ModGroup> result = await _modsFolderUseCase.AddToGroupAsync(
                ModsFolderPath.Trim(),
                paths,
                GroupNameInput.Trim());

            if (!result.Success)
            {
                AddToGroupStatusMessage = result.Error ?? "Could not add to group.";
                return;
            }

            string groupName = GroupNameInput;
            IsAddToGroupPanelVisible = false;
            ResetAddToGroupPanel();
            await LoadFilesCoreAsync();
            StatusMessage = $"Added {paths.Count} file(s) to \"{groupName}\".";
        }
        catch (Exception ex)
        {
            AddToGroupStatusMessage = $"Could not add to group: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelAddToGroup()
    {
        IsAddToGroupPanelVisible = false;
        ResetAddToGroupPanel();
    }

    private void ResetAddToGroupPanel()
    {
        GroupNameInput = string.Empty;
        AddToGroupStatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task UngroupSelectedAsync()
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
        StatusMessage = $"Removing {paths.Count} file(s) from their group...";

        try
        {
            await _modsFolderUseCase.RemoveFromGroupAsync(ModsFolderPath.Trim(), paths);
            await LoadFilesCoreAsync();
            StatusMessage = $"Removed {paths.Count} file(s) from their group.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to ungroup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void RequestInstallFromFile()
    {
        IsInstallPanelVisible = !IsInstallPanelVisible;
        if (!IsInstallPanelVisible)
        {
            ResetInstallPanel();
        }
    }

    [RelayCommand]
    private async Task PreviewInstallAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ArchivePathToInstall))
        {
            InstallStatusMessage = "Enter a file path first.";
            return;
        }

        IsBusy = true;
        HasArchivePreview = false;
        ArchivePreviewEntries.Clear();
        InstallStatusMessage = "Reading archive...";

        try
        {
            ArchiveInstallResult<ArchivePreview> result = await _archiveInstallService.PreviewAsync(ArchivePathToInstall.Trim());
            if (!result.Success)
            {
                InstallStatusMessage = result.Error ?? "Could not read the archive.";
                return;
            }

            foreach (ArchiveEntryPreview entry in result.Value!.Entries)
            {
                ArchivePreviewEntries.Add(new ArchiveEntryPreviewViewModel(entry));
            }

            InstallDisplayName = Path.GetFileNameWithoutExtension(ArchivePathToInstall.Trim());
            HasArchivePreview = true;
            InstallStatusMessage = ArchivePreviewEntries.Count(entry => entry.IsInstallable) == 0
                ? "No installable mod files found in this archive."
                : "Review the selection below, then install.";
        }
        catch (Exception ex)
        {
            InstallStatusMessage = $"Could not read the archive: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmInstallAsync()
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

        if (string.IsNullOrWhiteSpace(InstallDisplayName))
        {
            InstallStatusMessage = "Enter a name for this mod's folder.";
            return;
        }

        HashSet<string> selected = [.. ArchivePreviewEntries.Where(entry => entry.IsSelected).Select(entry => entry.EntryName)];
        if (selected.Count == 0)
        {
            InstallStatusMessage = "Select at least one file to install.";
            return;
        }

        IsBusy = true;
        InstallStatusMessage = "Installing...";

        try
        {
            ModsFolderLayout layout = _modsFolderUseCase.GetLayout(ModsFolderPath.Trim());
            string provider = _pendingInstallSourceUri is null ? "manual" : "browser";
            ArchiveInstallResult<InstallRecord> result = await _archiveInstallService.InstallAsync(
                ArchivePathToInstall.Trim(),
                selected,
                layout,
                InstallDisplayName.Trim(),
                new InstallSource(provider, _pendingInstallModPageUri?.ToString(), _pendingInstallSourceUri?.ToString()),
                version: null);

            if (!result.Success)
            {
                InstallStatusMessage = result.Error ?? "Install failed.";
                return;
            }

            string installedDisplayName = InstallDisplayName;
            IsInstallPanelVisible = false;
            ResetInstallPanel();
            await LoadFilesCoreAsync();
            StatusMessage = $"Installed {result.Value!.Files.Count} file(s) to \"{installedDisplayName}\".";
        }
        catch (Exception ex)
        {
            InstallStatusMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelInstall()
    {
        IsInstallPanelVisible = false;
        ResetInstallPanel();
    }

    private void ResetInstallPanel()
    {
        ArchivePathToInstall = string.Empty;
        InstallDisplayName = string.Empty;
        InstallStatusMessage = string.Empty;
        HasArchivePreview = false;
        ArchivePreviewEntries.Clear();
        _pendingInstallSourceUri = null;
        _pendingInstallModPageUri = null;
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
        IReadOnlyList<ModGroup> groups = await _modsFolderUseCase.LoadGroupsAsync(ModsFolderPath.Trim());
        _groups = [.. groups];
        ExistingGroupNames.Clear();
        foreach (string name in _groups.Select(group => group.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            ExistingGroupNames.Add(name);
        }

        ReplaceFiles(files);
        StatusMessage = files.Count == 0
            ? "No mod files found."
            : $"Loaded {files.Count} file(s).";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Files.Clear();
        FolderTree.Clear();
        SelectedTreeNodes.Clear();
        GroupTree.Clear();
        SelectedGroupNodes.Clear();

        List<ModFileViewModel> filtered = [.. string.IsNullOrWhiteSpace(SearchText)
            ? _allFiles
            : _allFiles.Where(file => file.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase))];

        foreach (ModFileViewModel file in filtered)
        {
            Files.Add(file);
        }

        foreach (ModTreeNodeViewModel node in ModTreeNodeViewModel.BuildTree(filtered))
        {
            FolderTree.Add(node);
        }

        // Group mode isn't filtered by search — it's a small, curated set of groups rather than a
        // big list you're hunting through, and a "missing" member wouldn't match a filter anyway.
        foreach (ModGroupNodeViewModel node in ModGroupNodeViewModel.BuildTree(_groups, _allFiles))
        {
            GroupTree.Add(node);
        }
    }

    private void SyncSelectedFilesFromTree()
    {
        HashSet<ModFileViewModel> files = [];
        foreach (ModTreeNodeViewModel node in SelectedTreeNodes)
        {
            CollectFiles(node, files);
        }

        SelectedFiles.Clear();
        foreach (ModFileViewModel file in files)
        {
            SelectedFiles.Add(file);
        }
    }

    private void SyncSelectedFilesFromGroupTree()
    {
        HashSet<ModFileViewModel> files = [];
        foreach (ModGroupNodeViewModel node in SelectedGroupNodes)
        {
            CollectGroupFiles(node, files);
        }

        SelectedFiles.Clear();
        foreach (ModFileViewModel file in files)
        {
            SelectedFiles.Add(file);
        }
    }

    private static void CollectGroupFiles(ModGroupNodeViewModel node, HashSet<ModFileViewModel> files)
    {
        if (node.File is not null)
        {
            files.Add(node.File);
            return;
        }

        if (node.IsMissing)
        {
            return;
        }

        foreach (ModGroupNodeViewModel child in node.Children)
        {
            CollectGroupFiles(child, files);
        }
    }

    private static void CollectFiles(ModTreeNodeViewModel node, HashSet<ModFileViewModel> files)
    {
        if (node.File is not null)
        {
            files.Add(node.File);
            return;
        }

        foreach (ModTreeNodeViewModel child in node.Children)
        {
            CollectFiles(child, files);
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
            DetailHeader = file.DisplayName is { Length: > 0 } ? $"{file.DisplayName} ({file.Name})" : file.Name;
            DetailBody = $"Folder: {(string.IsNullOrEmpty(file.Folder) ? "(root)" : file.Folder)}\n"
                + $"Size: {file.SizeBytes:N0} bytes\n"
                + $"Modified: {file.ModifiedUtc:u}\n"
                + $"Status: {file.StatusText}"
                + (file.Version is { Length: > 0 } ? $"\nVersion: {file.Version}" : string.Empty)
                + (file.InstalledUtc is { } installedUtc ? $"\nInstalled: {installedUtc:u}" : string.Empty)
                + (file.Provider is { Length: > 0 } ? $"\nSource: {file.Provider}" : string.Empty);
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
                new ModFile("Extras/ExtremeViolence.package", ModFileState.Disabled, 524_288, DateTime.UtcNow),
                new ModFile("Extras/Scripts/ExtremeViolence.ts4script", ModFileState.Disabled, 65_536, DateTime.UtcNow)
            ];
            return Task.FromResult(files);
        }

        public Task<IReadOnlyList<ModFileFailure>> EnableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModFileFailure>>([]);

        public Task<IReadOnlyList<ModFileFailure>> DisableAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModFileFailure>>([]);

        public Task<IReadOnlyList<ModFileFailure>> DeleteAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModFileFailure>>([]);

        public Task<ArchiveInstallResult<InstallRecord>> AdoptAsync(
            string modsFolderPath,
            IReadOnlyList<string> relativePaths,
            string displayName,
            string? modPageUrl,
            string? version,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<InstallRecord>.Fail("Not available at design time."));

        public Task<IReadOnlyList<ModGroup>> LoadGroupsAsync(string modsFolderPath, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModGroup>>([]);

        public Task<ArchiveInstallResult<ModGroup>> AddToGroupAsync(
            string modsFolderPath,
            IReadOnlyList<string> relativePaths,
            string groupName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<ModGroup>.Fail("Not available at design time."));

        public Task RemoveFromGroupAsync(string modsFolderPath, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class DesignTimeArchiveInstallService : IArchiveInstallService
    {
        public Task<ArchiveInstallResult<ArchivePreview>> PreviewAsync(string archivePath, CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<ArchivePreview>.Ok(new ArchivePreview([])));

        public Task<ArchiveInstallResult<InstallRecord>> InstallAsync(
            string archivePath,
            IReadOnlySet<string> selectedEntryNames,
            ModsFolderLayout layout,
            string displayName,
            InstallSource source,
            string? version,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<InstallRecord>.Fail("Not available at design time."));
    }
}
