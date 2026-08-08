using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ModManager.Ui.ViewModels;

public enum DownloadItemState
{
    InProgress,
    Completed,
    Canceled,
    Failed,
}

public partial class DownloadItemViewModel : ViewModelBase
{
    private readonly Action _cancelRequested;

    [ObservableProperty]
    private string _fileName;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private DownloadItemState _state = DownloadItemState.InProgress;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private string? _errorMessage;

    public DownloadItemViewModel(string fileName, Uri? sourceUri, Action cancelRequested)
    {
        _fileName = fileName;
        SourceUri = sourceUri;
        _cancelRequested = cancelRequested;
    }

    public Uri? SourceUri { get; }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancelRequested();

    private bool CanCancel() => State == DownloadItemState.InProgress;

    [RelayCommand(CanExecute = nameof(CanOpenContainingFolder))]
    private void OpenContainingFolder()
    {
        if (FilePath is null)
        {
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{FilePath}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", FilePath]);
            }
            else if (Path.GetDirectoryName(FilePath) is { } folder)
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch
        {
            // Best-effort; nothing sensible to do if the shell can't launch.
        }
    }

    private bool CanOpenContainingFolder() => State == DownloadItemState.Completed && FilePath is not null;

    public void MarkCompleted(string? filePath)
    {
        Progress = 1;
        State = DownloadItemState.Completed;
        FilePath = filePath;
        CancelCommand.NotifyCanExecuteChanged();
        OpenContainingFolderCommand.NotifyCanExecuteChanged();
    }

    public void MarkCanceled()
    {
        State = DownloadItemState.Canceled;
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void MarkFailed(string message)
    {
        State = DownloadItemState.Failed;
        ErrorMessage = message;
        CancelCommand.NotifyCanExecuteChanged();
    }
}
