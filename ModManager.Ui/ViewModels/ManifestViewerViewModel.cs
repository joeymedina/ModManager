using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModManager.Application.Models;
using ModManager.Ui.Models;

namespace ModManager.Ui.ViewModels;

public enum ManifestViewerTab
{
    Formatted,
    Raw,
}

/// <summary>
/// View over a mods folder's manifest: a formatted breakdown (files, groups, installs) alongside
/// the raw JSON text as it exists on disk. The raw text is editable — <see cref="HasUnsavedChanges"/>
/// tells the caller whether to offer saving it back. This view model never writes anything itself;
/// persisting an edit is the caller's job (see <c>SettingsPageViewModel.ViewManifestAsync</c>).
/// </summary>
public sealed partial class ManifestViewerViewModel : ViewModelBase
{
    private readonly string _originalRawJson;

    public string ManifestPath { get; }

    public bool ManifestExists { get; }

    public int SchemaVersion { get; }

    [ObservableProperty]
    private string _rawJson;

    /// <summary>True once the raw JSON text differs from what was loaded when the dialog opened.</summary>
    public bool HasUnsavedChanges => RawJson != _originalRawJson;

    public ObservableCollection<ManifestFileRow> Files { get; } = [];

    public ObservableCollection<ManifestGroupRow> Groups { get; } = [];

    public ObservableCollection<ManifestInstallRow> Installs { get; } = [];

    public bool HasFiles => Files.Count > 0;

    public bool HasGroups => Groups.Count > 0;

    public bool HasInstalls => Installs.Count > 0;

    [ObservableProperty]
    private ManifestViewerTab _selectedTab = ManifestViewerTab.Formatted;

    public ManifestViewerViewModel()
        : this(
            new ModsManifest(
                ModsManifest.CurrentSchemaVersion,
                [new ManifestFileEntry("WickedWhims_main.package", "WickedWhims", "group-1", null, "Animations")],
                [new ModGroup("group-1", "WickedWhims", ["WickedWhims_main.package"])],
                [
                    new InstallRecord(
                        "install-1",
                        new InstallSource("adopted", "https://example.com", null),
                        "170",
                        DateTime.UtcNow,
                        null,
                        [new InstallRecordFile("WickedWhims_main.package", "abc123", 1_048_576)],
                        [])
                ]),
            new ManifestRawContent(
                @"C:\Mods\.modmanager.json",
                true,
                "{\n  \"schemaVersion\": 1\n}"))
    {
    }

    public ManifestViewerViewModel(ModsManifest manifest, ManifestRawContent raw)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(raw);

        ManifestPath = raw.Path;
        ManifestExists = raw.Exists;
        SchemaVersion = manifest.SchemaVersion;
        _originalRawJson = raw.Json
            ?? "This mods folder doesn't have a manifest file yet. One is created the first time you install, adopt, or organize a mod.";
        _rawJson = _originalRawJson;

        Dictionary<string, string> groupNamesById = manifest.Groups.ToDictionary(group => group.GroupId, group => group.Name);
        foreach (ManifestFileEntry entry in manifest.Files)
        {
            string? groupName = entry.GroupId is not null
                ? groupNamesById.GetValueOrDefault(entry.GroupId, entry.GroupId)
                : null;
            Files.Add(new ManifestFileRow(entry.RelativePath, entry.DisplayName, groupName, entry.Category, entry.Notes));
        }

        foreach (ModGroup group in manifest.Groups)
        {
            Groups.Add(new ManifestGroupRow(group.Name, group.Members));
        }

        foreach (InstallRecord record in manifest.Installs)
        {
            Installs.Add(new ManifestInstallRow(
                record.Source.Provider,
                record.Version,
                record.InstalledUtc,
                record.Files.Count,
                record.Source.ModPageUrl,
                record.SourceArchivePath));
        }
    }

    [RelayCommand]
    private void SetTab(ManifestViewerTab tab) => SelectedTab = tab;
}
