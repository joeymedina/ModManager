namespace ModManager.Application.Models;

public enum ArchiveEntryKind
{
    Installable,
    Variant,
    NotInstallable,
}

public sealed record ArchiveEntryPreview(string EntryName, ArchiveEntryKind Kind, bool SelectedByDefault);

public sealed record ArchivePreview(IReadOnlyList<ArchiveEntryPreview> Entries);
