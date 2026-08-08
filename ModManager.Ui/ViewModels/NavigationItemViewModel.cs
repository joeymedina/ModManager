namespace ModManager.Ui.ViewModels;

public sealed class NavigationItemViewModel
{
    public NavigationItemViewModel(string title, ViewModelBase page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(page);

        Title = title;
        Page = page;
    }

    public string Title { get; }

    public ViewModelBase Page { get; }
}
