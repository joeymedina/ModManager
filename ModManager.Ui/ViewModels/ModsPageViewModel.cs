using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Ui.Services;

namespace ModManager.Ui.ViewModels;

public enum ModsListMode
{
    Flat,
    Folder,
    Group,
}

public enum ModsStatusFilter
{
    All,
    Enabled,
    Disabled,
}

public partial class ModsPageViewModel : ViewModelBase
{
    private static readonly string[] InstallableExtensions = [".zip", ".package", ".ts4script"];

    private readonly IModsFolderUseCase _modsFolderUseCase;
    private readonly IArchiveInstallService _archiveInstallService;
    private readonly IDialogService _dialogService;
    private readonly SettingsStore _settings;
    private readonly ILogger<ModsPageViewModel> _logger;
    private List<ModFileViewModel> _allFiles = [];
    private List<ModGroup> _groups = [];
    private Uri? _pendingInstallSourceUri;
    private Uri? _pendingInstallModPageUri;
    private string _previewedPath = string.Empty;

    public ObservableCollection<ModFileViewModel> Files { get; } = [];

    public ObservableCollection<ModFileViewModel> SelectedFiles { get; } = [];

    public ObservableCollection<ModTreeNodeViewModel> FolderTree { get; } = [];

    public ObservableCollection<ModTreeNodeViewModel> SelectedTreeNodes { get; } = [];

    public ObservableCollection<ModGroupNodeViewModel> GroupTree { get; } = [];

    public ObservableCollection<ModGroupNodeViewModel> SelectedGroupNodes { get; } = [];

    public ObservableCollection<ArchiveEntryPreviewViewModel> ArchivePreviewEntries { get; } = [];

    public ObservableCollection<string> ExistingGroupNames { get; } = [];

    public ObservableCollection<string> CategorySuggestions { get; } = [];

    public ObservableCollection<string> CategoryFilterOptions { get; } = ["All", "Uncategorized"];

    public IReadOnlyList<ModsListMode> ListModes { get; } =
        [ModsListMode.Flat, ModsListMode.Folder, ModsListMode.Group];

    [ObservableProperty]
    private ModsListMode _listMode = ModsListMode.Flat;

    [ObservableProperty]
    private ModsStatusFilter _statusFilter = ModsStatusFilter.All;

    [ObservableProperty]
    private string _categoryFilter = "All";

    [ObservableProperty]
    private string _modsFolderPath = string.Empty;

    [ObservableProperty]
    private string _disabledModsFolderPath = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Loading…";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private ModFileViewModel? _detailFile;

    [ObservableProperty]
    private string _detailHeader = "Nothing selected";

    [ObservableProperty]
    private string _detailSubtitle = "Pick a mod file to see its details.";

    [ObservableProperty]
    private string _archivePathToInstall = string.Empty;

    [ObservableProperty]
    private string _installDisplayName = string.Empty;

    [ObservableProperty]
    private string _installCategoryInput = string.Empty;

    [ObservableProperty]
    private bool _hasArchivePreview;

    [ObservableProperty]
    private string _installStatusMessage = string.Empty;

    [ObservableProperty]
    private string _adoptDisplayName = string.Empty;

    [ObservableProperty]
    private string _adoptVersion = string.Empty;

    [ObservableProperty]
    private string _adoptModPageUrl = string.Empty;

    [ObservableProperty]
    private string _adoptStatusMessage = string.Empty;

    [ObservableProperty]
    private string _groupNameInput = string.Empty;

    [ObservableProperty]
    private string _addToGroupStatusMessage = string.Empty;

    [ObservableProperty]
    private string _categoryInput = string.Empty;

    [ObservableProperty]
    private string _setCategoryStatusMessage = string.Empty;

    public bool HasSelection => SelectedCount > 0;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Just the folder name, for the page's breadcrumb-style header.</summary>
    public string ModsFolderName
    {
        get
        {
            string trimmed = ModsFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            return name.Length > 0 ? name : trimmed;
        }
    }

    public ModsPageViewModel()
        : this(new DesignTimeModsFolderUseCase(), new DesignTimeArchiveInstallService(), new NoopDialogService(), new SettingsStore())
    {
    }

    public ModsPageViewModel(
        IModsFolderUseCase modsFolderUseCase,
        IArchiveInstallService archiveInstallService,
        IDialogService dialogService,
        SettingsStore settings,
        ILogger<ModsPageViewModel>? logger = null)
    {
        _modsFolderUseCase = modsFolderUseCase;
        _archiveInstallService = archiveInstallService;
        _dialogService = dialogService;
        _settings = settings;
        _logger = logger ?? NullLogger<ModsPageViewModel>.Instance;

        ModsFolderPath = _settings.Load().ModsFolderPath ?? DefaultModsFolderPath();

        SelectedFiles.CollectionChanged += (_, _) => UpdateDetails();
        SelectedTreeNodes.CollectionChanged += (_, _) => SyncSelectedFilesFromTree();
        SelectedGroupNodes.CollectionChanged += (_, _) => SyncSelectedFilesFromGroupTree();
        UpdateLayoutPaths();
    }

    private static string DefaultModsFolderPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Electronic Arts",
        "The Sims 4",
        "Mods");

    /// <summary>
    /// Opens the install dialog pre-filled with a downloaded archive's path, so the browser's
    /// "Install to Mods" prompt lands the user straight at the file-selection step.
    /// </summary>
    public void BeginInstallFromFile(string archivePath, Uri? sourceUri = null, Uri? modPageUri = null)
    {
        _pendingInstallSourceUri = sourceUri;
        _pendingInstallModPageUri = modPageUri;
        _ = RunInstallFlowAsync(archivePath);
    }

    /// <summary>Applies a mods folder chosen elsewhere (the Settings page) and reloads.</summary>
    public async Task SetModsFolderAsync(string path)
    {
        ModsFolderPath = path;
        UpdateLayoutPaths();
        _settings.Save(_settings.Load() with { ModsFolderPath = ModsFolderPath });
        await RefreshAsync();
    }

    /// <summary>Loads the full manifest for the current mods folder, for the Settings page's manifest viewer.</summary>
    public Task<ModsManifest> LoadManifestAsync(CancellationToken cancellationToken = default) =>
        _modsFolderUseCase.LoadManifestAsync(ModsFolderPath, cancellationToken);

    /// <summary>Reads the current mods folder's manifest file as raw JSON text, for the Settings page's manifest viewer.</summary>
    public Task<ManifestRawContent> ReadManifestRawAsync(CancellationToken cancellationToken = default) =>
        _modsFolderUseCase.ReadManifestRawAsync(ModsFolderPath, cancellationToken);

    /// <summary>Parses and saves manually edited manifest JSON for the current mods folder.</summary>
    public Task<ArchiveInstallResult<ModsManifest>> SaveManifestRawAsync(string rawJson, CancellationToken cancellationToken = default) =>
        _modsFolderUseCase.SaveManifestRawAsync(ModsFolderPath, rawJson, cancellationToken);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ModsFolderPath))
        {
            ErrorMessage = "No mods folder is set. Choose one in Settings.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading mods…";

        try
        {
            await LoadFilesCoreAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load mods from {ModsFolderPath}", ModsFolderPath);
            ErrorMessage = $"Failed to load mods: {ex.Message}";
            StatusMessage = "Load failed.";
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

    /// <summary>Flips a single row's on/off switch without needing the selection bar.</summary>
    [RelayCommand]
    private async Task ToggleFileAsync(ModFileViewModel? file)
    {
        if (file is null || IsBusy)
        {
            return;
        }

        bool enabling = file.State != ModFileState.Enabled;
        IsBusy = true;

        try
        {
            IReadOnlyList<ModFileFailure> failures = enabling
                ? await _modsFolderUseCase.EnableAsync(ModsFolderPath.Trim(), [file.RelativePath], CancellationToken.None)
                : await _modsFolderUseCase.DisableAsync(ModsFolderPath.Trim(), [file.RelativePath], CancellationToken.None);

            if (failures.Count > 0)
            {
                // Put the switch back where it was — the file did not move.
                file.Refresh();
                ErrorMessage = $"Could not {(enabling ? "enable" : "disable")} {file.Name}: {failures[0].Reason}";
                return;
            }

            // Enable/disable only swaps which root the file sits under, so its relative path — and
            // every other column — is unchanged. Patch the one row instead of rescanning the folder
            // and rebuilding the list, which would drop the selection out from under the click.
            file.State = enabling ? ModFileState.Enabled : ModFileState.Disabled;
            file.Refresh();
            if (StatusFilter != ModsStatusFilter.All)
            {
                // The row no longer matches the active chip, so it has to leave the list.
                ApplyFilter();
            }

            StatusMessage = $"{(enabling ? "Enabled" : "Disabled")} {file.Name}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toggling {RelativePath} failed", file.RelativePath);
            ErrorMessage = $"Could not toggle {file.Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (!RequireSelection(out int count))
        {
            return;
        }

        bool confirmed = await _dialogService.ConfirmAsync(
            "Delete files?",
            $"Delete {count} file(s) permanently? This cannot be undone.",
            "Delete",
            isDestructive: true);

        if (!confirmed)
        {
            return;
        }

        await RunBulkActionAsync(
            "Deleting", "Deleted",
            (root, paths, ct) => _modsFolderUseCase.DeleteAsync(root, paths, ct));
    }

    [RelayCommand]
    private void SetStatusFilter(ModsStatusFilter filter) => StatusFilter = filter;

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void DismissError() => ErrorMessage = string.Empty;

    [RelayCommand]
    private void OpenModPage()
    {
        if (DetailFile?.ModPageUrl is not { Length: > 0 } url)
        {
            return;
        }

        LaunchShell(url);
    }

    [RelayCommand]
    private void RevealInExplorer()
    {
        if (DetailFile is not { } file)
        {
            return;
        }

        string root = file.State == ModFileState.Enabled ? ModsFolderPath : DisabledModsFolderPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        string folder = Path.GetDirectoryName(Path.Combine(root, file.RelativePath)) ?? root;
        LaunchShell(folder);
    }

    private void LaunchShell(string target)
    {
        try
        {
            using System.Diagnostics.Process? _ = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open {Target}", target);
            ErrorMessage = $"Could not open {target}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AdoptSelectedAsync()
    {
        if (!RequireSelection(out int count))
        {
            return;
        }

        AdoptDisplayName = SelectedFiles.Count == 1
            ? Path.GetFileNameWithoutExtension(SelectedFiles[0].Name)
            : string.Empty;
        AdoptVersion = string.Empty;
        AdoptModPageUrl = string.Empty;
        AdoptStatusMessage = string.Empty;

        if (!await _dialogService.ShowAsync("Adopt files", AppDialog.Adopt, this, "Adopt"))
        {
            return;
        }

        await ConfirmAdoptAsync(count);
    }

    private async Task ConfirmAdoptAsync(int count)
    {
        if (string.IsNullOrWhiteSpace(AdoptDisplayName))
        {
            ErrorMessage = "Adopt needs a name for the mod.";
            return;
        }

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        IsBusy = true;
        StatusMessage = "Adopting…";

        try
        {
            ArchiveInstallResult<InstallRecord> result = await _modsFolderUseCase.AdoptAsync(
                ModsFolderPath.Trim(),
                paths,
                AdoptDisplayName.Trim(),
                string.IsNullOrWhiteSpace(AdoptModPageUrl) ? null : AdoptModPageUrl.Trim(),
                string.IsNullOrWhiteSpace(AdoptVersion) ? null : AdoptVersion.Trim());

            if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Adopt failed.";
                StatusMessage = "Adopt failed.";
                return;
            }

            string adopted = AdoptDisplayName;
            await LoadFilesCoreAsync();
            StatusMessage = $"Adopted {count} file(s) as \"{adopted}\".";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adopt of {FileCount} file(s) as \"{DisplayName}\" failed", paths.Count, AdoptDisplayName);
            ErrorMessage = $"Adopt failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddToGroupAsync()
    {
        if (!RequireSelection(out int count))
        {
            return;
        }

        GroupNameInput = string.Empty;
        AddToGroupStatusMessage = string.Empty;

        if (!await _dialogService.ShowAsync("Add to group", AppDialog.AddToGroup, this, "Add"))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(GroupNameInput))
        {
            ErrorMessage = "A group needs a name.";
            return;
        }

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        IsBusy = true;
        StatusMessage = "Adding to group…";

        try
        {
            ArchiveInstallResult<ModGroup> result = await _modsFolderUseCase.AddToGroupAsync(
                ModsFolderPath.Trim(),
                paths,
                GroupNameInput.Trim());

            if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Could not add to group.";
                StatusMessage = "Could not add to group.";
                return;
            }

            string groupName = GroupNameInput;
            await LoadFilesCoreAsync();
            StatusMessage = $"Added {count} file(s) to \"{groupName}\".";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adding {FileCount} file(s) to group \"{GroupName}\" failed", paths.Count, GroupNameInput);
            ErrorMessage = $"Could not add to group: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UngroupSelectedAsync()
    {
        if (IsBusy || !RequireSelection(out int count))
        {
            return;
        }

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        IsBusy = true;
        StatusMessage = $"Removing {count} file(s) from their group…";

        try
        {
            await _modsFolderUseCase.RemoveFromGroupAsync(ModsFolderPath.Trim(), paths);
            await LoadFilesCoreAsync();
            StatusMessage = $"Removed {count} file(s) from their group.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ungrouping {FileCount} file(s) failed", paths.Count);
            ErrorMessage = $"Failed to ungroup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetCategoryAsync()
    {
        if (!RequireSelection(out int count))
        {
            return;
        }

        CategoryInput = string.Empty;
        SetCategoryStatusMessage = string.Empty;

        if (!await _dialogService.ShowAsync("Set category", AppDialog.SetCategory, this, "Set"))
        {
            return;
        }

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        IsBusy = true;
        StatusMessage = "Setting category…";

        try
        {
            ArchiveInstallResult<string?> result = await _modsFolderUseCase.SetCategoryAsync(
                ModsFolderPath.Trim(),
                paths,
                CategoryInput.Trim());

            if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Could not set category.";
                StatusMessage = "Could not set category.";
                return;
            }

            await LoadFilesCoreAsync();
            StatusMessage = result.Value is { } category
                ? $"Set category \"{category}\" on {count} file(s)."
                : $"Cleared category on {count} file(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setting category on {FileCount} file(s) failed", paths.Count);
            ErrorMessage = $"Could not set category: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallFromFileAsync()
    {
        string? picked = await _dialogService.PickFileAsync("Choose a mod archive", InstallableExtensions);
        if (picked is null)
        {
            return;
        }

        _pendingInstallSourceUri = null;
        _pendingInstallModPageUri = null;
        await RunInstallFlowAsync(picked);
    }

    /// <summary>Re-pick the archive from inside the install dialog, then re-run the preview.</summary>
    [RelayCommand]
    private async Task BrowseForArchiveAsync()
    {
        string? picked = await _dialogService.PickFileAsync("Choose a mod archive", InstallableExtensions);
        if (picked is null)
        {
            return;
        }

        ArchivePathToInstall = picked;
        await PreviewInstallAsync();
    }

    private async Task RunInstallFlowAsync(string archivePath)
    {
        ResetInstallPanel(keepSource: true);
        ArchivePathToInstall = archivePath;
        await PreviewInstallAsync();

        if (await _dialogService.ShowAsync("Install mod", AppDialog.Install, this, "Install"))
        {
            await ConfirmInstallAsync();
        }
        else
        {
            ResetInstallPanel();
        }
    }

    /// <summary>
    /// Re-reads the archive after the path box is edited by hand. Bound to the box losing focus
    /// rather than to every keystroke, since a preview opens and walks the whole archive.
    /// </summary>
    [RelayCommand]
    private async Task PreviewIfPathChangedAsync()
    {
        string path = ArchivePathToInstall.Trim();
        if (path.Length > 0 && !string.Equals(path, _previewedPath, StringComparison.OrdinalIgnoreCase))
        {
            await PreviewInstallAsync();
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
            InstallStatusMessage = "Choose a file first.";
            return;
        }

        // Recorded even if the read fails, so a bad path isn't retried on every blur.
        _previewedPath = ArchivePathToInstall.Trim();
        IsBusy = true;
        HasArchivePreview = false;
        ArchivePreviewEntries.Clear();
        InstallStatusMessage = "Reading archive…";

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
                : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not preview archive {ArchivePath}", ArchivePathToInstall);
            InstallStatusMessage = $"Could not read the archive: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConfirmInstallAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallDisplayName))
        {
            ErrorMessage = "Install needs a name for the mod's folder.";
            return;
        }

        HashSet<string> selected = [.. ArchivePreviewEntries.Where(entry => entry.IsSelected).Select(entry => entry.EntryName)];
        if (selected.Count == 0)
        {
            ErrorMessage = "No files were selected to install.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Installing…";

        try
        {
            ModsFolderLayout layout = _modsFolderUseCase.GetLayout(ModsFolderPath.Trim());
            string provider = _pendingInstallSourceUri is null ? "manual" : "browser";
            ArchiveInstallResult<InstallRecord> result = await _archiveInstallService.InstallAsync(
                ArchivePathToInstall.Trim(),
                selected,
                layout,
                InstallDisplayName.Trim(),
                string.IsNullOrWhiteSpace(InstallCategoryInput) ? null : InstallCategoryInput.Trim(),
                new InstallSource(provider, _pendingInstallModPageUri?.ToString(), _pendingInstallSourceUri?.ToString()),
                version: null);

            if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Install failed.";
                StatusMessage = "Install failed.";
                return;
            }

            string installed = InstallDisplayName;
            int fileCount = result.Value!.Files.Count;
            ResetInstallPanel();
            await LoadFilesCoreAsync();
            StatusMessage = $"Installed {fileCount} file(s) to \"{installed}\".";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install of {ArchivePath} as \"{DisplayName}\" failed", ArchivePathToInstall, InstallDisplayName);
            ErrorMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetInstallPanel(bool keepSource = false)
    {
        ArchivePathToInstall = string.Empty;
        InstallDisplayName = string.Empty;
        InstallCategoryInput = string.Empty;
        InstallStatusMessage = string.Empty;
        _previewedPath = string.Empty;
        HasArchivePreview = false;
        ArchivePreviewEntries.Clear();
        if (!keepSource)
        {
            _pendingInstallSourceUri = null;
            _pendingInstallModPageUri = null;
        }
    }

    private bool RequireSelection(out int count)
    {
        count = SelectedFiles.Count;
        if (count != 0)
        {
            return true;
        }

        StatusMessage = "Select one or more files first.";
        return false;
    }

    private async Task RunBulkActionAsync(
        string progressLabel,
        string resultLabel,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<ModFileFailure>>> action)
    {
        if (IsBusy || !RequireSelection(out _))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ModsFolderPath))
        {
            ErrorMessage = "No mods folder is set. Choose one in Settings.";
            return;
        }

        List<string> paths = [.. SelectedFiles.Select(file => file.RelativePath)];
        IsBusy = true;
        StatusMessage = $"{progressLabel} {paths.Count} file(s)…";

        try
        {
            IReadOnlyList<ModFileFailure> failures = await action(ModsFolderPath.Trim(), paths, CancellationToken.None);
            await LoadFilesCoreAsync();
            StatusMessage = $"{resultLabel} {paths.Count - failures.Count} file(s).";
            if (failures.Count > 0)
            {
                ErrorMessage = $"{failures.Count} file(s) failed: "
                    + string.Join("; ", failures.Select(failure => $"{failure.RelativePath}: {failure.Reason}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk {Operation} of {FileCount} file(s) failed", progressLabel, paths.Count);
            ErrorMessage = $"Failed while {progressLabel.ToLowerInvariant()}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

        List<string> usedCategories = [.. files
            .Select(file => file.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        CategorySuggestions.Clear();
        foreach (string name in ModCategories.Suggested.Union(usedCategories, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            CategorySuggestions.Add(name);
        }

        CategoryFilterOptions.Clear();
        CategoryFilterOptions.Add("All");
        CategoryFilterOptions.Add("Uncategorized");
        foreach (string name in usedCategories.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            CategoryFilterOptions.Add(name);
        }

        if (!CategoryFilterOptions.Contains(CategoryFilter))
        {
            CategoryFilter = "All";
        }

        ReplaceFiles(files);
        StatusMessage = files.Count == 0
            ? "No mod files found."
            : $"{files.Count} file(s) — {files.Count(file => file.State == ModFileState.Enabled)} enabled";
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnStatusFilterChanged(ModsStatusFilter value) => ApplyFilter();

    partial void OnCategoryFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasSelection));

    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnModsFolderPathChanged(string value) => OnPropertyChanged(nameof(ModsFolderName));

    private void ApplyFilter()
    {
        Files.Clear();
        FolderTree.Clear();
        SelectedTreeNodes.Clear();
        GroupTree.Clear();
        SelectedGroupNodes.Clear();

        IEnumerable<ModFileViewModel> query = _allFiles;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(file => file.RelativePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        query = StatusFilter switch
        {
            ModsStatusFilter.Enabled => query.Where(file => file.State == ModFileState.Enabled),
            ModsStatusFilter.Disabled => query.Where(file => file.State != ModFileState.Enabled),
            _ => query,
        };

        query = CategoryFilter switch
        {
            "All" => query,
            "Uncategorized" => query.Where(file => string.IsNullOrEmpty(file.Category)),
            _ => query.Where(file => string.Equals(file.Category, CategoryFilter, StringComparison.OrdinalIgnoreCase)),
        };

        List<ModFileViewModel> filtered = [.. query];

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

        ReplaceSelection(files);
    }

    private void SyncSelectedFilesFromGroupTree()
    {
        HashSet<ModFileViewModel> files = [];
        foreach (ModGroupNodeViewModel node in SelectedGroupNodes)
        {
            CollectGroupFiles(node, files);
        }

        ReplaceSelection(files);
    }

    private void ReplaceSelection(HashSet<ModFileViewModel> files)
    {
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
        SelectedCount = SelectedFiles.Count;

        if (SelectedFiles.Count == 0)
        {
            DetailFile = null;
            DetailHeader = "Nothing selected";
            DetailSubtitle = "Pick a mod file to see its details.";
        }
        else if (SelectedFiles.Count == 1)
        {
            ModFileViewModel file = SelectedFiles[0];
            DetailFile = file;
            DetailHeader = file.DisplayName is { Length: > 0 } name ? name : file.Name;
            DetailSubtitle = file.DisplayName is { Length: > 0 } ? file.Name : file.FolderText;
        }
        else
        {
            DetailFile = null;
            DetailHeader = $"{SelectedFiles.Count} files selected";
            long totalBytes = SelectedFiles.Sum(file => file.SizeBytes);
            DetailSubtitle = $"{totalBytes / 1024.0 / 1024.0:0.#} MB total";
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
        catch (Exception ex)
        {
            // Without a disabled root, enable/disable silently misbehaves — say so rather than
            // leaving the user to discover it when a command does nothing.
            _logger.LogWarning(ex, "Could not resolve the mods folder layout for {ModsFolderPath}", ModsFolderPath);
            DisabledModsFolderPath = string.Empty;
            ErrorMessage = $"Could not resolve that mods folder path: {ex.Message}";
        }
    }

    private sealed class NoopDialogService : IDialogService
    {
        public Task<bool> ShowAsync(string title, AppDialog dialog, object dataContext, string primaryText)
            => Task.FromResult(false);

        public Task<bool> ConfirmAsync(string title, string message, string primaryText, bool isDestructive = false)
            => Task.FromResult(false);

        public Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions, string filterLabel = "Mod files") => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title, string? startPath) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension, string filterLabel) => Task.FromResult<string?>(null);
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

        public Task<ArchiveInstallResult<string?>> SetCategoryAsync(
            string modsFolderPath,
            IReadOnlyList<string> relativePaths,
            string? category,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<string?>.Fail("Not available at design time."));

        public Task<ModsManifest> LoadManifestAsync(string modsFolderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(ModsManifest.Empty);

        public Task<ManifestRawContent> ReadManifestRawAsync(string modsFolderPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new ManifestRawContent(Path.Combine(modsFolderPath, ".modmanager.json"), false, null));

        public Task<ArchiveInstallResult<ModsManifest>> SaveManifestRawAsync(string modsFolderPath, string rawJson, CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<ModsManifest>.Fail("Not available at design time."));

        public Task<ArchiveInstallResult<InstallRecord>> RenameInstallFolderAsync(
            string modsFolderPath,
            string installId,
            string desiredFolderName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<InstallRecord>.Fail("Not available at design time."));
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
            string? category,
            InstallSource source,
            string? version,
            InstallRecord? supersedes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ArchiveInstallResult<InstallRecord>.Fail("Not available at design time."));
    }
}
