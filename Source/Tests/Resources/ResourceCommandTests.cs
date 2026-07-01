using Celbridge.FileSystem.Services;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Unit tests for GetFileInfoCommand, GetFileTreeCommand, and ListFolderContentsCommand.
/// Uses a real ResourceRegistry rooted at a temp folder rather than mocking the registry,
/// because the commands read file-system metadata (size, mtime) and mocking that would
/// require shadowing half of System.IO.
/// </summary>
[TestFixture]
public class ResourceCommandTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private RootHandlerRegistry _rootHandlerRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    private const string FolderName = "Folder";
    private const string RootFileName = "root.txt";
    private const string NestedFileName = "nested.md";
    private const string BinaryFileName = "image.png";
    private const string FileContents = "Line one\nLine two\nLine three\n";

    [SetUp]
    public async Task Setup()
    {
        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ResourceCommandTests)}/{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectFolderPath);

        // Create a small tree:
        //   root.txt
        //   image.png
        //   Folder/
        //     nested.md
        File.WriteAllText(Path.Combine(_projectFolderPath, RootFileName), FileContents);
        File.WriteAllBytes(Path.Combine(_projectFolderPath, BinaryFileName), new byte[] { 0, 1, 2, 3, 4, 5 });
        var subFolder = Path.Combine(_projectFolderPath, FolderName);
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, NestedFileName), FileContents);

        var messengerService = new MessengerService();
        var iconService = new IconService();
        _rootHandlerRegistry = new RootHandlerRegistry();
        _resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, ProjectTreeBuilderTestHelper.Build(_projectFolderPath, iconService), ResourceClassifierTestHelper.BuildEmptyStub(), _rootHandlerRegistry, TestFileSystem.CreateLocal());
        _resourceRegistry.InitializeProjectRoot(_projectFolderPath);
        await _resourceRegistry.UpdateResourceRegistryAsync();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.RootHandlers.Returns(_rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // ListFolderContentsCommand and GetFileTreeCommand route through the
        // LocalResourceFileSystem gateway, so the workspace needs a real instance
        // (a Substitute would return null for EnumerateFolderAsync).
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
    public async Task GetFileInfo_ForTextFile_ReportsTextAndLineCount()
    {
        var textBinarySniffer = new TextBinarySniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
        var command = new GetFileInfoCommand(_workspaceWrapper, textBinarySniffer)
        {
            Resource = new ResourceKey(RootFileName)
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var snapshot = command.ResultValue;
        snapshot.Exists.Should().BeTrue();
        snapshot.IsFile.Should().BeTrue();
        snapshot.IsText.Should().BeTrue();
        snapshot.LineCount.Should().Be(3);
        snapshot.Extension.Should().Be(".txt");
        snapshot.Size.Should().Be(FileContents.Length);
    }

    [Test]
    public async Task GetFileInfo_ForBinaryFile_ReportsBinaryWithNoLineCount()
    {
        var textBinarySniffer = new TextBinarySniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
        var command = new GetFileInfoCommand(_workspaceWrapper, textBinarySniffer)
        {
            Resource = new ResourceKey(BinaryFileName)
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var snapshot = command.ResultValue;
        snapshot.Exists.Should().BeTrue();
        snapshot.IsFile.Should().BeTrue();
        snapshot.IsText.Should().BeFalse();
        snapshot.LineCount.Should().BeNull();
        snapshot.Extension.Should().Be(".png");
    }

    [Test]
    public async Task GetFileInfo_ForFolder_ReportsExistsButNotFile()
    {
        var textBinarySniffer = new TextBinarySniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
        var command = new GetFileInfoCommand(_workspaceWrapper, textBinarySniffer)
        {
            Resource = new ResourceKey(FolderName)
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var snapshot = command.ResultValue;
        snapshot.Exists.Should().BeTrue();
        snapshot.IsFile.Should().BeFalse();
        snapshot.LineCount.Should().BeNull();
    }

    [Test]
    public async Task ListFolderContents_ForRoot_ReturnsAllChildren()
    {
        var command = new ListFolderContentsCommand(_workspaceWrapper)
        {
            Resource = ResourceKey.Empty
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var entries = command.ResultValue.Entries;

        entries.Select(entry => entry.Name).Should().Contain(new[] { RootFileName, BinaryFileName, FolderName });
        var folderEntry = entries.Single(entry => entry.Name == FolderName);
        folderEntry.IsFolder.Should().BeTrue();
        folderEntry.Size.Should().Be(0);

        var rootFileEntry = entries.Single(entry => entry.Name == RootFileName);
        rootFileEntry.IsFolder.Should().BeFalse();
        rootFileEntry.Size.Should().Be(FileContents.Length);
    }

    [Test]
    public async Task ListFolderContents_ForNonFolderResource_Fails()
    {
        var command = new ListFolderContentsCommand(_workspaceWrapper)
        {
            Resource = new ResourceKey(RootFileName)
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task GetFileTree_ForRoot_ReturnsFullTreeWithinDepth()
    {
        var command = new GetFileTreeCommand(_workspaceWrapper)
        {
            Resource = ResourceKey.Empty,
            Depth = 3
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var root = command.ResultValue.Root;
        root.Should().NotBeNull();
        root!.IsFolder.Should().BeTrue();

        var childNames = root.Children.Select(childNode => childNode.Name).ToList();
        childNames.Should().Contain(new[] { FolderName, RootFileName, BinaryFileName });

        var subFolder = root.Children.Single(childNode => childNode.Name == FolderName);
        subFolder.IsFolder.Should().BeTrue();
        subFolder.Children.Should().HaveCount(1);
        subFolder.Children[0].Name.Should().Be(NestedFileName);
    }

    [Test]
    public async Task GetFileTree_WithDepthOne_TruncatesNestedFolders()
    {
        var command = new GetFileTreeCommand(_workspaceWrapper)
        {
            Resource = ResourceKey.Empty,
            Depth = 1
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var root = command.ResultValue.Root;
        Guard.IsNotNull(root);
        var subFolder = root.Children.SingleOrDefault(childNode => childNode.Name == FolderName);
        Guard.IsNotNull(subFolder);

        // Depth=1 means the root's children are listed, but the sub-folder's children are not.
        subFolder.Truncated.Should().BeTrue();
        subFolder.Children.Should().BeEmpty();
    }

    [Test]
    public async Task GetFileTree_WithGlob_FiltersFilesByPattern()
    {
        var command = new GetFileTreeCommand(_workspaceWrapper)
        {
            Resource = ResourceKey.Empty,
            Depth = 3,
            Glob = "*.md"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var root = command.ResultValue.Root;
        Guard.IsNotNull(root);

        // No .md files at the root level, so only the Folder/ subtree survives.
        root.Children.Where(childNode => !childNode.IsFolder).Should().BeEmpty();

        var subFolder = root.Children.Single(childNode => childNode.Name == FolderName);
        subFolder.Children.Should().ContainSingle(childNode => childNode.Name == NestedFileName);
    }

    [Test]
    public async Task GetFileTree_WithFileOnlyFilter_OmitsFolders()
    {
        var command = new GetFileTreeCommand(_workspaceWrapper)
        {
            Resource = ResourceKey.Empty,
            Depth = 3,
            TypeFilter = "file"
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var root = command.ResultValue.Root;
        Guard.IsNotNull(root);
        root.Children.Should().OnlyContain(childNode => !childNode.IsFolder);
    }

    // Registers a logs: root backed by a fresh temp folder pre-populated with the
    // supplied entries (string == file with that name, ending in "/" == folder).
    // Returns the backing path so the caller can clean up.
    private string SetupLogsRoot(params string[] entries)
    {
        var logsBacking = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ResourceCommandTests)}_logs/{Guid.NewGuid():N}");
        Directory.CreateDirectory(logsBacking);
        foreach (var entry in entries)
        {
            var fullPath = Path.Combine(logsBacking, entry);
            if (entry.EndsWith('/'))
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, "log content");
            }
        }
        _rootHandlerRegistry.RegisterRootHandler(new LogsRootHandler(logsBacking));
        return logsBacking;
    }

    [Test]
    public async Task ListFolderContents_ForLogsRoot_ReturnsBackingFolderChildren()
    {
        // Regression for the logs: enumeration bug. Before the fix, this returned
        // "Resource not found" because the in-memory tree is project-only.
        var logsBacking = SetupLogsRoot("session.log", "errors/", "errors/today.log");
        try
        {
            var command = new ListFolderContentsCommand(_workspaceWrapper)
            {
                Resource = new ResourceKey("logs:")
            };

            var result = await command.ExecuteAsync();

            result.IsSuccess.Should().BeTrue();
            var entries = command.ResultValue.Entries;
            entries.Select(entry => entry.Name).Should().BeEquivalentTo(new[] { "session.log", "errors" });

            var errorsEntry = entries.Single(entry => entry.Name == "errors");
            errorsEntry.IsFolder.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(logsBacking, recursive: true);
        }
    }

    [Test]
    public async Task GetFileTree_ForLogsRoot_WalksBackingFolderRecursively()
    {
        var logsBacking = SetupLogsRoot("session.log", "errors/", "errors/today.log", "errors/yesterday.log");
        try
        {
            var command = new GetFileTreeCommand(_workspaceWrapper)
            {
                Resource = new ResourceKey("logs:"),
                Depth = 3
            };

            var result = await command.ExecuteAsync();

            result.IsSuccess.Should().BeTrue();
            var root = command.ResultValue.Root;
            Guard.IsNotNull(root);

            var errorsNode = root.Children.Single(childNode => childNode.Name == "errors");
            errorsNode.IsFolder.Should().BeTrue();
            errorsNode.Children.Select(childNode => childNode.Name)
                .Should().BeEquivalentTo(new[] { "today.log", "yesterday.log" });
        }
        finally
        {
            Directory.Delete(logsBacking, recursive: true);
        }
    }
}
