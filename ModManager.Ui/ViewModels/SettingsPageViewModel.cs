using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Ui.Services;

namespace ModManager.Ui.ViewModels;

public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly ModsPageViewModel _mods;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _modsFolderPath;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string DisabledModsFolderPath => _mods.DisabledModsFolderPath;

    public SettingsPageViewModel()
        : this(new ModsPageViewModel(), new NoopDialogService())
    {
    }

    public SettingsPageViewModel(ModsPageViewModel mods, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(mods);

        _mods = mods;
        _dialogService = dialogService;
        _modsFolderPath = mods.ModsFolderPath;
        _mods.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModsPageViewModel.DisabledModsFolderPath))
            {
                OnPropertyChanged(nameof(DisabledModsFolderPath));
            }
        };
    }

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
        public Task<bool> ShowAsync(string title, ModsDialog dialog, object dataContext, string primaryText)
            => Task.FromResult(false);

        public Task<bool> ConfirmAsync(string title, string message, string primaryText, bool isDestructive = false)
            => Task.FromResult(false);

        public Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title, string? startPath) => Task.FromResult<string?>(null);
    }
}
