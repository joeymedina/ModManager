using Avalonia.Controls;
using Avalonia.Input;

namespace ModManager.Ui.Views;

public partial class ModsPageView : UserControl
{
    public ModsPageView()
    {
        InitializeComponent();
    }

    // AutoCompleteBox's MinimumPrefixLength="0" only opens the dropdown once a TextChanged
    // event fires, so focusing an empty box shows nothing until you type — force it open on
    // focus so every existing group is visible to pick from immediately.
    private void OnGroupNameBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is AutoCompleteBox box)
        {
            box.IsDropDownOpen = true;
        }
    }
}
