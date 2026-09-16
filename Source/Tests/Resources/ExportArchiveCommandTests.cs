using System.IO.Compression;
using Celbridge.Dialog;
using Celbridge.Localization;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ExportArchiveCommand over a real resource file system, writing to a folder outside the project.
/// Each test starts from the empty file the save dialog can create at the chosen path.
/// </summary>
[TestFixture]
public class ExportArchiveCommandTests
{
    private string _testFolderPath = null!;
    private string _projectFolderPath = null!;
    private string _exportFolderPath = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _testFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ExportArchiveCommandTests),
            Guid.NewGuid().ToString("N"));
        _projectFolderPath = Path.Combine(_testFolderPath, "Acme");
        _exportFolderPath = Path.Combine(_testFolderPath, "Exports");
        Directory.CreateDirectory(_projectFolderPath);
        Directory.CreateDirectory(_exportFolderPath);

        var projectFolderPath = _projectFolderPath;
        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(projectFolderPath);
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var resourceKey = callInfo.Arg<ResourceKey>();
            var relativePath = resourceKey.Path.Replace('/', Path.DirectorySeparatorChar);
            var absolutePath = string.IsNullOrEmpty(relativePath)
                ? projectFolderPath
                : Path.Combine(projectFolderPath, relativePath);

            return Result<string>.Ok(absolutePath);
        });
        _resourceRegistry.GetResourceKey(Arg.Any<string>())
            .Returns(Result<ResourceKey>.Fail("Path is not under the project folder"));

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.Registry.Returns(_resourceRegistry);
        _resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        _resourceService.FileSystem.Returns(resourceFileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testFolderPath))
        {
            Directory.Delete(_testFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task ExportProjectFolder_ReplacesDestinationWithArchiveWithoutReservedFolders()
    {
        WriteProjectFile("Acme.celbridge");
        WriteProjectFile("readme.md");
        WriteProjectFile("Folder/nested.md");
        WriteProjectFile(".celbridge/settings/workspace_settings.json");
        WriteProjectFile(".git/config");

        var archiveFilePath = CreateEmptyFile(_exportFolderPath, "Acme.zip");

        var result = await ExportProjectFolderAsync(archiveFilePath);

        result.IsSuccess.Should().BeTrue();
        ReadEntryNames(archiveFilePath).Should().BeEquivalentTo(new[]
        {
            "Acme.celbridge",
            "readme.md",
            "Folder/nested.md"
        });

        // The temporary file the archive was written to has been moved into place.
        Directory.GetFiles(_exportFolderPath).Should().ContainSingle();
    }

    [Test]
    public async Task ExportProjectFolder_IntoProjectFolder_LeavesOutTheArchiveItself()
    {
        WriteProjectFile("readme.md");

        var archiveFilePath = CreateEmptyFile(_projectFolderPath, "Acme.zip");
        _resourceRegistry.GetResourceKey(archiveFilePath)
            .Returns(Result<ResourceKey>.Ok(new ResourceKey("Acme.zip")));

        var result = await ExportProjectFolderAsync(archiveFilePath);

        result.IsSuccess.Should().BeTrue();
        ReadEntryNames(archiveFilePath).Should().BeEquivalentTo(new[]
        {
            "readme.md"
        });
    }

    [Test]
    public async Task ExportProjectFolder_WhenAFileCannotBeRead_LeavesNoFilesBehind()
    {
        var readmeResource = new ResourceKey("readme.md");
        var folderInfo = new StorageItemInfo(StorageItemKind.Folder, 0, DateTime.UtcNow, FileSystemAttributes.None);
        var folderItems = new List<FolderItem>
        {
            new FolderItem(readmeResource, IsFolder: false, Size: 5, ModifiedUtc: DateTime.UtcNow, Attributes: FileSystemAttributes.None)
        };

        var resourceFileSystem = Substitute.For<IResourceFileSystem>();
        resourceFileSystem.GetInfoAsync(ResourceKey.Empty)
            .Returns(Task.FromResult(Result<StorageItemInfo>.Ok(folderInfo)));
        resourceFileSystem.EnumerateFolderAsync(ResourceKey.Empty)
            .Returns(Task.FromResult(Result<IReadOnlyList<FolderItem>>.Ok(folderItems)));
        resourceFileSystem.OpenReadAsync(readmeResource)
            .Returns(Task.FromResult(Result<Stream>.Fail("The file is locked")));
        _resourceService.FileSystem.Returns(resourceFileSystem);

        var archiveFilePath = CreateEmptyFile(_exportFolderPath, "Acme.zip");

        var result = await ExportProjectFolderAsync(archiveFilePath);

        result.IsFailure.Should().BeTrue();
        Directory.GetFiles(_exportFolderPath).Should().BeEmpty();
    }

    private async Task<Result> ExportProjectFolderAsync(string archiveFilePath)
    {
        var operationNotifier = new ResourceOperationNotifier(
            Substitute.For<ILogger<ResourceOperationNotifier>>(),
            Substitute.For<IMessengerService>(),
            Substitute.For<IProjectService>(),
            Substitute.For<IReportWriter>(),
            Substitute.For<ILocalizerService>());

        var command = new ExportArchiveCommand(
            Substitute.For<ILogger<ExportArchiveCommand>>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal(),
            Substitute.For<IDialogService>(),
            Substitute.For<ILocalizerService>(),
            operationNotifier)
        {
            SourceResource = ResourceKey.Empty,
            ArchiveFilePath = archiveFilePath
        };

        return await command.ExecuteAsync();
    }

    private void WriteProjectFile(string relativePath)
    {
        var filePath = Path.Combine(_projectFolderPath, relativePath);
        var folderPath = Path.GetDirectoryName(filePath);
        Guard.IsNotNull(folderPath);

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, relativePath);
    }

    private static string CreateEmptyFile(string folderPath, string fileName)
    {
        var filePath = Path.Combine(folderPath, fileName);
        File.WriteAllBytes(filePath, Array.Empty<byte>());

        return filePath;
    }

    private static List<string> ReadEntryNames(string archiveFilePath)
    {
        using var zipArchive = ZipFile.OpenRead(archiveFilePath);

        return zipArchive.Entries
            .Select(entry => entry.FullName)
            .ToList();
    }
}
