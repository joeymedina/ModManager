using ModManager.Application.Models;
using ModManager.Ui.ViewModels;

namespace ModManager.Tests.Ui.ViewModels;

[TestClass]
public sealed class ModGroupNodeViewModelTests
{
    private static ModFileViewModel CreateFile(string relativePath) =>
        new(new ModFile(relativePath, ModFileState.Enabled, SizeBytes: 0, ModifiedUtc: DateTime.UtcNow));

    [TestMethod]
    public void BuildTree_WhenMemberMatchesALoadedFile_ThenNodeWrapsThatFile()
    {
        ModFileViewModel file = CreateFile("Alpha.package");
        ModGroup group = new("group-1", "MyGroup", ["Alpha.package"]);

        IReadOnlyList<ModGroupNodeViewModel> tree = ModGroupNodeViewModel.BuildTree([group], [file]);

        ModGroupNodeViewModel member = tree.Single().Children.Single();
        Assert.IsFalse(member.IsMissing);
        Assert.AreSame(file, member.File);
        Assert.AreEqual("Alpha.package", member.Name);
    }

    [TestMethod]
    public void BuildTree_WhenMemberHasNoMatchingFile_ThenNodeIsMissingWithSuffixAndMissingPathSet()
    {
        ModGroup group = new("group-1", "MyGroup", ["Gone/Alpha.package"]);

        IReadOnlyList<ModGroupNodeViewModel> tree = ModGroupNodeViewModel.BuildTree([group], []);

        ModGroupNodeViewModel member = tree.Single().Children.Single();
        Assert.IsTrue(member.IsMissing);
        Assert.AreEqual("Alpha.package (missing)", member.Name);
        Assert.AreEqual("Gone/Alpha.package", member.MissingPath);
        Assert.IsNull(member.File);
    }

    [TestMethod]
    public void BuildTree_WhenGroupsGiven_ThenOrdersCaseInsensitivelyByName()
    {
        ModGroup zebra = new("group-1", "zebra", []);
        ModGroup apple = new("group-2", "Apple", []);

        IReadOnlyList<ModGroupNodeViewModel> tree = ModGroupNodeViewModel.BuildTree([zebra, apple], []);

        CollectionAssert.AreEqual(new[] { "Apple", "zebra" }, tree.Select(node => node.Name).ToArray());
    }
}
