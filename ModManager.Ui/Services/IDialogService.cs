namespace ModManager.Ui.Services;

/// <summary>Which piece of dialog content to host. Keeps view types out of the view models.</summary>
public enum ModsDialog
{
    Install,
    Adopt,
    AddToGroup,
}

public interface IDialogService
{
    /// <summary>Shows <paramref name="dialog"/> bound to <paramref name="dataContext"/>. True when the user confirmed.</summary>
    Task<bool> ShowAsync(string title, ModsDialog dialog, object dataContext, string primaryText);

    Task<bool> ConfirmAsync(string title, string message, string primaryText, bool isDestructive = false);

    Task<string?> PickFileAsync(string title, IReadOnlyList<string> extensions);

    Task<string?> PickFolderAsync(string title, string? startPath);
}
