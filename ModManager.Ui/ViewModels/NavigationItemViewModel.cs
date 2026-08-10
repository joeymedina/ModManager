using FluentAvalonia.UI.Controls;

namespace ModManager.Ui.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string title, ViewModelBase page, FASymbol icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(page);

        Title = title;
        Page = page;
        Icon = icon;
    }

    public string Title { get; }

    public ViewModelBase Page { get; }

    public FASymbol Icon { get; }

    /// <summary>NavigationView binds an icon source, not a bare symbol.</summary>
    public FASymbolIconSource IconSource => new() { Symbol = Icon };
}
