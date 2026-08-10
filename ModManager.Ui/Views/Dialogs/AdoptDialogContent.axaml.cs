using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModManager.Ui.Views.Dialogs;

public partial class AdoptDialogContent : UserControl
{
    public AdoptDialogContent()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
