using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ModManager.Ui.Views.Dialogs;

public partial class SetCategoryDialogContent : UserControl
{
    public SetCategoryDialogContent()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Same reasoning as AddToGroupDialogContent.OnGroupNameBoxGotFocus: force the dropdown open on
    // focus while empty so the suggested categories are visible immediately, and never do it
    // synchronously to avoid re-entering the popup's own teardown.
    private void OnCategoryBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is not AutoCompleteBox { IsDropDownOpen: false, Text: null or "" } box)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (box.IsKeyboardFocusWithin && !box.IsDropDownOpen && string.IsNullOrEmpty(box.Text))
                {
                    box.IsDropDownOpen = true;
                }
            },
            DispatcherPriority.Input);
    }
}
