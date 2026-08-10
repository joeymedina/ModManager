using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModManager.Ui.Views.Dialogs;

public partial class ThemeEditorDialogContent : UserControl
{
    public ThemeEditorDialogContent()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
