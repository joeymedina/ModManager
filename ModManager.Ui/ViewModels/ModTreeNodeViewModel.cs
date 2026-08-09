using System.Collections.ObjectModel;

namespace ModManager.Ui.ViewModels;

public sealed class ModTreeNodeViewModel(string name, ModFileViewModel? file)
{
    public string Name { get; } = name;

    public ModFileViewModel? File { get; } = file;

    public bool IsFolder => File is null;

    public ObservableCollection<ModTreeNodeViewModel> Children { get; } = [];

    public static IReadOnlyList<ModTreeNodeViewModel> BuildTree(IEnumerable<ModFileViewModel> files)
    {
        var root = new ModTreeNodeViewModel(string.Empty, null);
        Dictionary<string, ModTreeNodeViewModel> folders = new() { [string.Empty] = root };

        foreach (ModFileViewModel file in files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            ModTreeNodeViewModel parent = GetOrCreateFolder(file.Folder, folders);
            parent.Children.Add(new ModTreeNodeViewModel(file.Name, file));
        }

        return [.. root.Children];
    }

    private static ModTreeNodeViewModel GetOrCreateFolder(string folderPath, Dictionary<string, ModTreeNodeViewModel> folders)
    {
        if (folders.TryGetValue(folderPath, out ModTreeNodeViewModel? existing))
        {
            return existing;
        }

        int lastSlash = folderPath.LastIndexOf('/');
        string parentPath = lastSlash < 0 ? string.Empty : folderPath[..lastSlash];
        string name = lastSlash < 0 ? folderPath : folderPath[(lastSlash + 1)..];

        ModTreeNodeViewModel parent = GetOrCreateFolder(parentPath, folders);
        var node = new ModTreeNodeViewModel(name, null);
        parent.Children.Add(node);
        folders[folderPath] = node;
        return node;
    }
}
