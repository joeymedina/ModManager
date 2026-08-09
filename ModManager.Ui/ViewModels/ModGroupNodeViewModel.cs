using System.Collections.ObjectModel;
using ModManager.Application.Models;

namespace ModManager.Ui.ViewModels;

public sealed class ModGroupNodeViewModel
{
    public string Name { get; }

    public ModFileViewModel? File { get; }

    public string? MissingPath { get; }

    public bool IsMissing => MissingPath is not null;

    public ObservableCollection<ModGroupNodeViewModel> Children { get; } = [];

    private ModGroupNodeViewModel(string name, ModFileViewModel? file, string? missingPath)
    {
        Name = name;
        File = file;
        MissingPath = missingPath;
    }

    public static IReadOnlyList<ModGroupNodeViewModel> BuildTree(IReadOnlyList<ModGroup> groups, IReadOnlyList<ModFileViewModel> files)
    {
        Dictionary<string, ModFileViewModel> filesByPath = files
            .ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);

        List<ModGroupNodeViewModel> nodes = [];
        foreach (ModGroup group in groups.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase))
        {
            var node = new ModGroupNodeViewModel(group.Name, null, null);
            foreach (string memberPath in group.Members)
            {
                node.Children.Add(filesByPath.TryGetValue(memberPath, out ModFileViewModel? file)
                    ? new ModGroupNodeViewModel(file.Name, file, null)
                    : new ModGroupNodeViewModel($"{Path.GetFileName(memberPath)} (missing)", null, memberPath));
            }

            nodes.Add(node);
        }

        return nodes;
    }
}
