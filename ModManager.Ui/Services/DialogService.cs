using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using ModManager.Ui.Views.Dialogs;

namespace ModManager.Ui.Services;

public sealed class DialogService : IDialogService
{
    public async Task<bool> ShowAsync(string title, ModsDialog dialog, object dataContext, string primaryText)
    {
        Control content = dialog switch
        {
            ModsDialog.Install => new InstallDialogContent(),
            ModsDialog.Adopt => new AdoptDialogContent(),
            ModsDialog.AddToGroup => new AddToGroupDialogContent(),
            _ => throw new ArgumentOutOfRangeException(nameof(dialog)),
        };

        content.DataContext = dataContext;

        FAContentDialog contentDialog = new()
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = FAContentDialogButton.Primary,
        };

        return await ShowCoreAsync(contentDialog) == FAContentDialogResult.Primary;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, bool isDestructive = false)
    {
        FAContentDialog dialog = new()
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            // Destructive actions default to Cancel so a stray Enter can't delete anything.
            DefaultButton = isDestructive ? FAContentDialogButton.Close : FAContentDialogButton.Primary,
        };

        return await ShowCoreAsync(dialog) == FAContentDialogResult.Primary;
    }

    public async Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions)
    {
        if (GetStorageProvider() is not { } storage)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Mod files")
                {
                    Patterns = [.. extensions.Select(extension => $"*{extension}")],
                },
                FilePickerFileTypes.All,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title, string? startPath)
    {
        if (GetStorageProvider() is not { } storage)
        {
            return null;
        }

        IStorageFolder? start = null;
        if (!string.IsNullOrWhiteSpace(startPath))
        {
            try
            {
                start = await storage.TryGetFolderFromPathAsync(startPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A bad remembered path just means the picker opens wherever the OS prefers.
            }
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static async Task<FAContentDialogResult> ShowCoreAsync(FAContentDialog dialog)
    {
        if (MainWindow is not { } window)
        {
            return FAContentDialogResult.None;
        }

        return await dialog.ShowAsync(window);
    }

    private static IStorageProvider? GetStorageProvider() => MainWindow?.StorageProvider;

    private static Window? MainWindow =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
