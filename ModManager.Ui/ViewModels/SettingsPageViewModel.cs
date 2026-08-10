using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Ui.Models;
using ModManager.Ui.Services;

namespace ModManager.Ui.ViewModels;

public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly ModsPageViewModel _mods;
    private readonly IDialogService _dialogService;
    private readonly ThemeService _themeService;
    private readonly SettingsStore _settings;

    [ObservableProperty]
    private string _modsFolderPath;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<AppTheme> AvailableThemes { get; } = [];

    [ObservableProperty]
    private AppTheme _selectedTheme;

    public string DisabledModsFolderPath => _mods.DisabledModsFolderPath;

    public SettingsPageViewModel()
        : this(new ModsPageViewModel(), new NoopDialogService(), new ThemeService(), new SettingsStore())
    {
    }

    public SettingsPageViewModel(
        ModsPageViewModel mods,
        IDialogService dialogService,
        ThemeService themeService,
        SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(settings);

        _mods = mods;
        _dialogService = dialogService;
        _themeService = themeService;
        _settings = settings;
        _modsFolderPath = mods.ModsFolderPath;
        _mods.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModsPageViewModel.DisabledModsFolderPath))
            {
                OnPropertyChanged(nameof(DisabledModsFolderPath));
            }
        };

        foreach (AppTheme theme in _themeService.ListThemes())
        {
            AvailableThemes.Add(theme);
        }

        string? themeName = _settings.Load().ThemeName;
        _selectedTheme = AvailableThemes.FirstOrDefault(theme => theme.Name == themeName) ?? ThemePresets.DefaultLight;
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        // The ComboBox's SelectedItem binding can hand back null when the item it's bound to is
        // removed from AvailableThemes mid-selection (e.g. deleting the current theme) — the
        // AppTheme type says non-null, but Avalonia's binding engine doesn't enforce that at runtime.
        if (value is null)
        {
            return;
        }

        _themeService.Apply(value);
        _settings.Save(_settings.Load() with { ThemeName = value.Name });
        DeleteThemeCommand.NotifyCanExecuteChanged();
    }

    private bool CanDeleteTheme() => !ThemePresets.All.Any(preset => preset.Name == SelectedTheme.Name);

    [RelayCommand]
    private async Task EditThemeAsync()
    {
        AppTheme original = SelectedTheme;
        string seedName = ThemePresets.All.Any(preset => preset.Name == original.Name)
            ? $"Copy of {original.Name}"
            : original.Name;
        ThemeEditorViewModel editor = new(_themeService, original with { Name = seedName });

        bool confirmed = await _dialogService.ShowAsync("Edit theme", AppDialog.ThemeEditor, editor, "Save");
        if (!confirmed)
        {
            _themeService.Apply(original);
            return;
        }

        AppTheme edited = editor.ToTheme();
        if (string.IsNullOrWhiteSpace(edited.Name))
        {
            StatusMessage = "Give the theme a name first.";
            _themeService.Apply(original);
            return;
        }

        try
        {
            _themeService.Save(edited);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            _themeService.Apply(original);
            return;
        }

        AppTheme? existing = AvailableThemes.FirstOrDefault(theme => theme.Name == edited.Name);
        if (existing is not null)
        {
            AvailableThemes.Remove(existing);
        }

        AvailableThemes.Add(edited);
        SelectedTheme = edited;
        StatusMessage = $"Saved \"{edited.Name}\".";
    }

    [RelayCommand]
    private void DuplicateTheme()
    {
        string baseName = $"Copy of {SelectedTheme.Name}";
        string name = baseName;
        int suffix = 2;
        while (AvailableThemes.Any(theme => theme.Name == name))
        {
            name = $"{baseName} ({suffix++})";
        }

        AppTheme copy = SelectedTheme with { Name = name };
        _themeService.Save(copy);
        AvailableThemes.Add(copy);
        SelectedTheme = copy;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteTheme))]
    private async Task DeleteThemeAsync()
    {
        bool confirmed = await _dialogService.ConfirmAsync(
            "Delete theme?",
            $"Delete \"{SelectedTheme.Name}\" permanently? This cannot be undone.",
            "Delete",
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        AppTheme toDelete = SelectedTheme;
        _themeService.Delete(toDelete.Name);
        SelectedTheme = ThemePresets.DefaultLight;
        AvailableThemes.Remove(toDelete);
    }

    [RelayCommand]
    private async Task ImportThemeAsync()
    {
        string? path = await _dialogService.PickFileAsync("Import theme", [".json"], "Theme files");
        if (path is null)
        {
            return;
        }

        AppTheme? imported = _themeService.Import(path);
        if (imported is null)
        {
            StatusMessage = "That file isn't a valid theme.";
            return;
        }

        if (ThemePresets.All.Any(preset => preset.Name == imported.Name))
        {
            imported = imported with { Name = $"{imported.Name} (imported)" };
        }

        _themeService.Save(imported);

        AppTheme? existing = AvailableThemes.FirstOrDefault(theme => theme.Name == imported.Name);
        if (existing is not null)
        {
            AvailableThemes.Remove(existing);
        }

        AvailableThemes.Add(imported);
        SelectedTheme = imported;
        StatusMessage = $"Imported \"{imported.Name}\".";
    }

    [RelayCommand]
    private async Task ExportThemeAsync()
    {
        string? path = await _dialogService.SaveFileAsync(
            "Export theme", $"{SelectedTheme.Name}.json", ".json", "Theme files");
        if (path is null)
        {
            return;
        }

        _themeService.Export(SelectedTheme, path);
        StatusMessage = $"Exported to {path}.";
    }

    [RelayCommand]
    private void ResetTheme() => SelectedTheme = ThemePresets.DefaultLight;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? picked = await _dialogService.PickFolderAsync("Choose your Mods folder", ModsFolderPath);
        if (picked is not null)
        {
            ModsFolderPath = picked;
            await ApplyAsync();
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (string.IsNullOrWhiteSpace(ModsFolderPath))
        {
            StatusMessage = "Enter a folder path first.";
            return;
        }

        await _mods.SetModsFolderAsync(ModsFolderPath.Trim());
        ModsFolderPath = _mods.ModsFolderPath;
        StatusMessage = "Saved. The Mods page has been reloaded.";
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
}
