namespace ModManager.Application.Models;

public sealed record ModUpdateRequest(string ModId, string ModsFolder, bool DownloadIfUpdateAvailable);
