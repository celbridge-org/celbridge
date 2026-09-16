using System.IO.Compression;
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
/// Tests for ArchiveResourceCommand over a real resource file system, so an archive holds exactly the
/// files the resource policy lets the command read.
/// </summary>
[TestFixture]
public class ArchiveResourceCommandTests
{
    private string _projectFolderPath = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private byte[]? _createdFileContent;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ArchiveResourceCommandTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        var projectFolderPath = _projectFolderPath;
        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ProjectFolderPath.Returns(projectFolderPath);
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var resourceKey = callInfo.Arg<ResourceKey>();
            var relativePath = resourceKey.Path.Replace('/', Path.DirectorySeparatorChar);
            var absolutePath = string.IsNullOrEmpty(relativePath)
                ? projectFolderPath
                : Path.Combine(projectFolderPath, relativePath);

            return Result<string>.Ok(absolutePath);
        });

        // The command hands the finished archive to CreateFileAsync, so a test reads it from these bytes.
        var operationService = Substitute.For<IResourceOperationService>();
        operationService.CreateFileAsync(Arg.Any<ResourceKey>(), Arg.Do<byte[]>(content => _createdFileContent = content))
            .Returns(Task.FromResult(Result.Ok()));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);
        resourceService.Operations.Returns(operationService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task ArchiveProjectFolder_LeavesOutReservedFolders()
    {
        WriteProjectFile("Acme.celbridge");
        WriteProjectFile("readme.md");
        WriteProjectFile("Folder/nested.md");
        WriteProjectFile(".celbridge/settings/workspace_settings.json");
        WriteProjectFile(".git/config");

        var command = CreateCommand();
        command.SourceResource = ResourceKey.Empty;
        command.ArchiveResource = new ResourceKey("Acme.zip");

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        Guard.IsNotNull(_createdFileContent);

        using var archiveStream = new MemoryStream(_createdFileContent);
        using var zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var entryNames = zipArchive.Entries
            .Select(entry => entry.FullName)
            .ToList();

        entryNames.Should().BeEquivalentTo(new[]
        {
            "Acme.celbridge",
            "readme.md",
            "Folder/nested.md"
        });
        command.ResultValue.Entries.Should().Be(3);
    }

    private ArchiveResourceCommand CreateCommand()
    {
        var operationNotifier = new ResourceOperationNotifier(
            Substitute.For<ILogger<ResourceOperationNotifier>>(),
            Substitute.For<IMessengerService>(),
            Substitute.For<IProjectService>(),
            Substitute.For<IReportWriter>(),
            Substitute.For<ILocalizerService>());

        return new ArchiveResourceCommand(
            Substitute.For<ILogger<ArchiveResourceCommand>>(),
            _workspaceWrapper,
            operationNotifier);
    }

    private void WriteProjectFile(string relativePath)
    {
        var filePath = Path.Combine(_projectFolderPath, relativePath);
        var folderPath = Path.GetDirectoryName(filePath);
        Guard.IsNotNull(folderPath);

        Directory.CreateDirectory(folderPath);
        File.WriteAllText(filePath, relativePath);
    }
}
