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
    public async Task LoadFilesAsync_WhenRepositoryReturnsFiles_ThenReturnsSameResult()
    {
        var expected = (IReadOnlyList<ModFile>)[new ModFile("WW_main.package", ModFileState.Enabled, 100, DateTime.UtcNow)];
        var repositoryMock = new Mock<IModsFolderRepository>(MockBehavior.Strict);

        repositoryMock
            .Setup(repository => repository.LoadFilesAsync("C:/Mods", CancellationToken.None))
            .ReturnsAsync(expected);

        var useCase = new ModsFolderUseCase(repositoryMock.Object);

        IReadOnlyList<ModFile> actual = await useCase.LoadFilesAsync("C:/Mods", CancellationToken.None);

        Assert.AreSame(expected, actual);
        repositoryMock.Verify(repository => repository.LoadFilesAsync("C:/Mods", CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task DisableAsync_WhenRepositoryReturnsFailures_ThenReturnsSameResult()
    {
        IReadOnlyList<string> paths = ["WW_main.package"];
        var expected = (IReadOnlyList<ModFileFailure>)[new ModFileFailure("Missing.package", "File not found.")];
        var repositoryMock = new Mock<IModsFolderRepository>(MockBehavior.Strict);

        repositoryMock
            .Setup(repository => repository.DisableAsync("C:/Mods", paths, CancellationToken.None))
            .ReturnsAsync(expected);

        var useCase = new ModsFolderUseCase(repositoryMock.Object);

        IReadOnlyList<ModFileFailure> actual = await useCase.DisableAsync("C:/Mods", paths, CancellationToken.None);

        Assert.AreSame(expected, actual);
        repositoryMock.Verify(repository => repository.DisableAsync("C:/Mods", paths, CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task SetCategoryAsync_WhenRepositoryReturnsResult_ThenReturnsSameResult()
    {
        IReadOnlyList<string> paths = ["WW_main.package"];
        ArchiveInstallResult<string?> expected = ArchiveInstallResult<string?>.Ok("Scripts");
        var repositoryMock = new Mock<IModsFolderRepository>(MockBehavior.Strict);

        repositoryMock
            .Setup(repository => repository.SetCategoryAsync("C:/Mods", paths, "Scripts", CancellationToken.None))
            .ReturnsAsync(expected);

        var useCase = new ModsFolderUseCase(repositoryMock.Object);

        ArchiveInstallResult<string?> actual = await useCase.SetCategoryAsync("C:/Mods", paths, "Scripts", CancellationToken.None);

        Assert.AreSame(expected, actual);
        repositoryMock.Verify(repository => repository.SetCategoryAsync("C:/Mods", paths, "Scripts", CancellationToken.None), Times.Once);
    }
}
