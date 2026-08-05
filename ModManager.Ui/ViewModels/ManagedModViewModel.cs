using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public partial class ManagedModViewModel : ViewModelBase
{
    private readonly ModPageViewModel _owner;

    [ObservableProperty]
    private string _modId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _packageKey = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isMixedState;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _canEnable;

    [ObservableProperty]
    private bool _canDisable;

    public ManagedModViewModel(ModPageViewModel owner, ManagedMod mod)
    {
        _owner = owner;
        Apply(mod);
    }

    public void Apply(ManagedMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);

        ModId = mod.ModId;
        Name = mod.Name;
        PackageKey = mod.PackageKey;
        IsEnabled = mod.IsEnabled;
        IsMixedState = mod.IsMixedState;
        FileCount = mod.Files.Count;
        StatusText = mod.IsMixedState
            ? "Mixed"
            : mod.IsEnabled
                ? "Enabled"
                : "Disabled";
        CanEnable = mod.IsMixedState || !mod.IsEnabled;
        CanDisable = mod.IsMixedState || mod.IsEnabled;
    }

    [RelayCommand]
    private Task EnableAsync() => _owner.EnableModAsync(this);

    [RelayCommand]
    private Task DisableAsync() => _owner.DisableModAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteModAsync(this);
}
