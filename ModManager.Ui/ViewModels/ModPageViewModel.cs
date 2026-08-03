using System.Collections.ObjectModel;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public partial class ModPageViewModel : ViewModelBase
{
    public ObservableCollection<ManagedMod> Mods { get; } = new();

   public ModPageViewModel()
    {
        // Sample data for demonstration purposes
        Mods.Add(new ManagedMod(string.Empty,"WickedWhims", "",null, false));
        Mods.Add(new ManagedMod(string.Empty,"ExtremeViolence", "",null, false));
        Mods.Add(new ManagedMod(string.Empty,"Mod 3", "",null, false));
    }
}
