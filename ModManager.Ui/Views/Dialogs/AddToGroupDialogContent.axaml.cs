using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ModManager.Ui.Views.Dialogs;

public partial class AddToGroupDialogContent : UserControl
{
    public AddToGroupDialogContent()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // AutoCompleteBox's MinimumPrefixLength="0" only opens the dropdown once a TextChanged
    // event fires, so focusing an empty box shows nothing until you type — force it open on
    // focus so every existing group is visible to pick from immediately.
    //
    // Only while the box is still empty, and never synchronously: picking a suggestion closes the
    // dropdown and hands focus back here, so opening it again inline would re-enter the popup's own
    // teardown and mutate the visual tree it is mid-way through walking (ArgumentOutOfRangeException).
    private void OnGroupNameBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is not AutoCompleteBox { IsDropDownOpen: false, Text: null or "" } box)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                // Focus lands on the inner TextBox, so IsFocused on the box itself is never true.
                if (box.IsKeyboardFocusWithin && !box.IsDropDownOpen && string.IsNullOrEmpty(box.Text))
                {
                    box.IsDropDownOpen = true;
                }
            },
            DispatcherPriority.Input);
    }
}
