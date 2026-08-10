using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ModManager.Ui.ViewModels;

namespace ModManager.Ui.Views.Dialogs;

public partial class InstallDialogContent : UserControl
{
    public InstallDialogContent()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // Picking a file previews it automatically, so a typed path should too — just not on every
    // keystroke, since each preview opens and walks the archive.
    private void OnArchivePathCommitted(object? sender, RoutedEventArgs e) => Preview();

    // Enter would otherwise hit the dialog's Install button, installing whatever the previous
    // preview held rather than the path just typed.
    private void OnArchivePathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Preview();
        }
    }

    private void Preview()
    {
        if (DataContext is ModsPageViewModel viewModel)
        {
            viewModel.PreviewIfPathChangedCommand.Execute(null);
        }
    }
}
