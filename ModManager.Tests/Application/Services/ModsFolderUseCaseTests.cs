using Moq;
using ModManager.Application.Interfaces;
using ModManager.Application.Models;
using ModManager.Application.Services;

namespace ModManager.Tests.Application.Services;

[TestClass]
public sealed class ModsFolderUseCaseTests
{
    [TestMethod]
    public void GetLayout_WhenModsFolderIsWhitespace_ThenThrowsArgumentException()
    {
        var repositoryMock = new Mock<IModsFolderRepository>(MockBehavior.Strict);
        var useCase = new ModsFolderUseCase(repositoryMock.Object);

        Assert.ThrowsExactly<ArgumentException>(() => useCase.GetLayout("  "));
    }

    [TestMethod]
    public async Task LoadModsAsync_WhenRepositoryReturnsMods_ThenReturnsSameResult()
    {
        var expected = (IReadOnlyList<ManagedMod>)[new ManagedMod("abc", "WW", "ww", [new ManagedModFile("WW_main.package", ModFileState.Enabled)], false)];
        var repositoryMock = new Mock<IModsFolderRepository>(MockBehavior.Strict);

        repositoryMock
            .Setup(repository => repository.LoadModsAsync("C:/Mods", CancellationToken.None))
            .ReturnsAsync(expected);

        var useCase = new ModsFolderUseCase(repositoryMock.Object);

        IReadOnlyList<ManagedMod> actual = await useCase.LoadModsAsync("C:/Mods", CancellationToken.None);

        Assert.AreSame(expected, actual);
        repositoryMock.Verify(repository => repository.LoadModsAsync("C:/Mods", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DisableModAsync_WhenRepositoryReturnsUpdatedMod_ThenReturnsMod()
    {
        var expected = new ManagedMod("mod-1", "WW", "ww", [new ManagedModFile("WW_main.package", ModFileState.Disabled)], false);
        var repositoryMock = new Mock<IModsFolderRepository>(MockBehavior.Strict);

        repositoryMock
            .Setup(repository => repository.DisableModAsync("C:/Mods", "mod-1", CancellationToken.None))
            .ReturnsAsync(expected);

        var useCase = new ModsFolderUseCase(repositoryMock.Object);

        ManagedMod actual = await useCase.DisableModAsync("C:/Mods", "mod-1", CancellationToken.None);

        Assert.AreEqual(expected, actual);
        repositoryMock.Verify(repository => repository.DisableModAsync("C:/Mods", "mod-1", CancellationToken.None), Times.Once);
    }
}
