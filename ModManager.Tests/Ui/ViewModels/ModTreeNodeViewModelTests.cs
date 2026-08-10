using ModManager.Application.Models;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class ModTreeNodeViewModelTests
{
    private static ModFileViewModel CreateFile(string relativePath) =>
        new(new ModFile(relativePath, ModFileState.Enabled, SizeBytes: 0, ModifiedUtc: DateTime.UtcNow));

    [TestMethod]
    public void BuildTree_WhenFileIsAtRoot_ThenItAttachesDirectlyToTheTopLevelList()
    {
        ModFileViewModel file = CreateFile("root.package");

        IReadOnlyList<ModTreeNodeViewModel> tree = ModTreeNodeViewModel.BuildTree([file]);

        ModTreeNodeViewModel node = tree.Single();
        Assert.IsFalse(node.IsFolder);
        Assert.AreSame(file, node.File);
    }

    [TestMethod]
    public void BuildTree_WhenFileIsNested_ThenBuildsNestedFolderNodes()
    {
        ModFileViewModel file = CreateFile("Extras/Sub/foo.package");

        IReadOnlyList<ModTreeNodeViewModel> tree = ModTreeNodeViewModel.BuildTree([file]);

        ModTreeNodeViewModel extras = tree.Single();
        Assert.IsTrue(extras.IsFolder);
        Assert.AreEqual("Extras", extras.Name);

        ModTreeNodeViewModel sub = extras.Children.Single();
        Assert.IsTrue(sub.IsFolder);
        Assert.AreEqual("Sub", sub.Name);

        ModTreeNodeViewModel leaf = sub.Children.Single();
        Assert.IsFalse(leaf.IsFolder);
        Assert.AreSame(file, leaf.File);
    }

    [TestMethod]
    public void BuildTree_WhenFilesShareAFolder_ThenTheyLandUnderOneSharedFolderNodeNotDuplicates()
    {
        ModFileViewModel first = CreateFile("Extras/a.package");
        ModFileViewModel second = CreateFile("Extras/b.package");

        IReadOnlyList<ModTreeNodeViewModel> tree = ModTreeNodeViewModel.BuildTree([first, second]);

        ModTreeNodeViewModel extras = tree.Single();
        Assert.HasCount(2, extras.Children);
        Assert.IsTrue(extras.Children.All(child => !child.IsFolder));
    }

    [TestMethod]
    public void BuildTree_WhenFilesGiven_ThenOrdersCaseInsensitivelyByRelativePath()
    {
        ModFileViewModel zebra = CreateFile("zebra.package");
        ModFileViewModel apple = CreateFile("Apple.package");

        IReadOnlyList<ModTreeNodeViewModel> tree = ModTreeNodeViewModel.BuildTree([zebra, apple]);

        CollectionAssert.AreEqual(new[] { "Apple.package", "zebra.package" }, tree.Select(node => node.Name).ToArray());
    }
}
