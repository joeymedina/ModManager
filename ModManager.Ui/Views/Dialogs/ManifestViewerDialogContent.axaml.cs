using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ModManager.Ui.Views.Dialogs;

public partial class ManifestViewerDialogContent : UserControl
{
    public ManifestViewerDialogContent()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
