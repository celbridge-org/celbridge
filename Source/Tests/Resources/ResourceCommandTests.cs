using Celbridge.FileSystem.Services;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Packages;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Unit tests for GetFileInfoCommand, GetFileTreeCommand, and ListFolderContentsCommand.
/// Uses a real resource file system rooted at a temp folder. The commands read sizes and
/// modification times from it.
/// </summary>
[TestFixture]
public class ResourceCommandTests
{
    private string _projectFolderPath = null!;
    private RootHandlerRegistry _rootHandlerRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    private const string FolderName = "Folder";
    private const string EmptyFolderName = "Empty";
    private const string OtherFolderName = "Other";
    private const string RootFileName = "root.txt";
    private const string NestedFileName = "nested.md";
    private const string BinaryFileName = "image.png";
    private const string FileContents = "Line one\nLine two\nLine three\n";

    [SetUp]
    public void Setup()
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

        _rootHandlerRegistry = TestRootHandlers.Create(_projectFolderPath);

        var resourceService = Substitute.For<IResourceService>();
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
        resourceService.Sidecars.Returns(new SidecarService(_workspaceWrapper));
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
        var textBinarySniffer = TestFileTypeCatalog.CreateSniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
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
        var textBinarySniffer = TestFileTypeCatalog.CreateSniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
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
        var textBinarySniffer = TestFileTypeCatalog.CreateSniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
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
    public async Task GetFileInfo_ForAFileWithAHealthySidecar_ReportsTheSidecar()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, RootFileName + ".cel"), "editor = \"celbridge.code-editor\"\n");

        var snapshot = await GetFileInfoAsync(RootFileName);

        snapshot.SidecarKey.Should().Be(new ResourceKey(RootFileName + ".cel"));
        snapshot.SidecarStatus.Should().Be(CelParseStatus.Healthy);
    }

    [Test]
    public async Task GetFileInfo_ForAFileWithABrokenSidecar_ReportsItBroken()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, RootFileName + ".cel"), "editor = \n");

        var snapshot = await GetFileInfoAsync(RootFileName);

        snapshot.SidecarKey.Should().Be(new ResourceKey(RootFileName + ".cel"));
        snapshot.SidecarStatus.Should().Be(CelParseStatus.Broken);
    }

    [Test]
    public async Task GetFileInfo_ForAFileWithoutASidecar_ReportsNoSidecar()
    {
        var snapshot = await GetFileInfoAsync(RootFileName);

        snapshot.SidecarKey.Should().BeNull();
        snapshot.SidecarStatus.Should().BeNull();
    }

    [Test]
    public async Task GetFileInfo_ForASidecarFile_ReportsNoSidecarOfItsOwn()
    {
        var sidecarName = RootFileName + ".cel";
        File.WriteAllText(Path.Combine(_projectFolderPath, sidecarName), "editor = \"celbridge.code-editor\"\n");

        var snapshot = await GetFileInfoAsync(sidecarName);

        snapshot.SidecarKey.Should().BeNull();
        snapshot.SidecarStatus.Should().BeNull();
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
        var root = await GetFileTreeRootAsync(depth: 3);

        root.IsFolder.Should().BeTrue();

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
        var root = await GetFileTreeRootAsync(depth: 1);

        var subFolder = root.Children.SingleOrDefault(childNode => childNode.Name == FolderName);
        Guard.IsNotNull(subFolder);

        // Depth=1 means the root's children are listed, but the sub-folder's children are not.
        subFolder.Truncated.Should().BeTrue();
        subFolder.Children.Should().BeEmpty();
    }

    [Test]
    public async Task GetFileTree_WithGlob_FiltersFilesByPattern()
    {
        var root = await GetFileTreeRootAsync(depth: 3, glob: "*.md");

        // No .md files at the root level, so only the Folder/ subtree survives.
        root.Children.Where(childNode => !childNode.IsFolder).Should().BeEmpty();

        var subFolder = root.Children.Single(childNode => childNode.Name == FolderName);
        subFolder.Children.Should().ContainSingle(childNode => childNode.Name == NestedFileName);
    }

    [Test]
    public async Task GetFileTree_WithGlobMatchingNothing_ReturnsEmptyRootWithItsName()
    {
        var root = await GetFileTreeRootAsync(depth: 3, glob: "*.json");

        root.Name.Should().Be(ResourceKey.DefaultRoot);
        root.IsFolder.Should().BeTrue();
        root.Children.Should().BeEmpty();
    }

    [Test]
    public async Task GetFileTree_WithGlobAtDepthLimit_LeavesOutFoldersTheGlobCannotReach()
    {
        var otherFolder = Path.Combine(_projectFolderPath, OtherFolderName);
        Directory.CreateDirectory(otherFolder);
        File.WriteAllText(Path.Combine(otherFolder, "other.md"), FileContents);

        var root = await GetFileTreeRootAsync(depth: 1, glob: "Folder/*.md");

        // The glob could match inside Folder, so Folder stays as a truncated node. The glob cannot match inside Other.
        var subFolder = root.Children.Should().ContainSingle().Subject;
        subFolder.Name.Should().Be(FolderName);
        subFolder.Truncated.Should().BeTrue();
    }

    [Test]
    public async Task GetFileTree_WithDepthZero_TruncatesRoot()
    {
        var root = await GetFileTreeRootAsync(depth: 0);

        root.Truncated.Should().BeTrue();
        root.Children.Should().BeEmpty();
    }

    [Test]
    public async Task GetFileTree_WithFileOnlyFilter_KeepsFoldersThatContainFiles()
    {
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, EmptyFolderName));

        var root = await GetFileTreeRootAsync(depth: 3, typeFilter: "file");

        // The empty folder has no file below it, so the tree leaves the folder out.
        var expectedNames = new[]
        {
            FolderName,
            RootFileName,
            BinaryFileName
        };
        root.Children.Select(childNode => childNode.Name).Should().BeEquivalentTo(expectedNames);

        var subFolder = root.Children.Single(childNode => childNode.Name == FolderName);
        subFolder.Children.Should().ContainSingle(childNode => childNode.Name == NestedFileName);
    }

    [Test]
    public async Task GetFileTree_WithFileOnlyFilterAndGlob_FindsNestedMatches()
    {
        var root = await GetFileTreeRootAsync(depth: 3, glob: "*.md", typeFilter: "file");

        var subFolder = root.Children.Should().ContainSingle().Subject;
        subFolder.Name.Should().Be(FolderName);
        subFolder.Children.Should().ContainSingle(childNode => childNode.Name == NestedFileName);
    }

    [Test]
    public async Task GetFileTree_WithFolderOnlyFilter_KeepsEveryFolder()
    {
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, EmptyFolderName));

        var root = await GetFileTreeRootAsync(depth: 3, typeFilter: "folder");

        var expectedNames = new[]
        {
            FolderName,
            EmptyFolderName
        };
        root.Children.Select(childNode => childNode.Name).Should().BeEquivalentTo(expectedNames);

        var subFolder = root.Children.Single(childNode => childNode.Name == FolderName);
        subFolder.Children.Should().BeEmpty();
    }

    [Test]
    public async Task GetFileTree_WithFolderOnlyFilterAndGlob_KeepsFoldersThatContainMatches()
    {
        var otherFolder = Path.Combine(_projectFolderPath, OtherFolderName);
        Directory.CreateDirectory(otherFolder);
        File.WriteAllText(Path.Combine(otherFolder, "other.txt"), FileContents);

        var root = await GetFileTreeRootAsync(depth: 3, glob: "*.md", typeFilter: "folder");

        var subFolder = root.Children.Should().ContainSingle().Subject;
        subFolder.Name.Should().Be(FolderName);
        subFolder.Children.Should().BeEmpty();
    }

    [Test]
    public async Task GetFileTree_TypeFilterIgnoresCase()
    {
        var root = await GetFileTreeRootAsync(depth: 3, typeFilter: "FOLDER");

        var subFolder = root.Children.Should().ContainSingle().Subject;
        subFolder.Name.Should().Be(FolderName);
    }

    [Test]
    public async Task GetFileTree_WithUnknownTypeFilter_Fails()
    {
        var command = new GetFileTreeCommand(_workspaceWrapper)
        {
            Resource = ResourceKey.Empty,
            TypeFilter = "files"
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("'files'");
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
        // Listing the logs: root returns the children of the root's backing folder.
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

    private async Task<FileInfoSnapshot> GetFileInfoAsync(string path)
    {
        var textBinarySniffer = TestFileTypeCatalog.CreateSniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
        var command = new GetFileInfoCommand(_workspaceWrapper, textBinarySniffer)
        {
            Resource = new ResourceKey(path)
        };

        var result = await command.ExecuteAsync();
        result.IsSuccess.Should().BeTrue();

        return command.ResultValue;
    }

    // Runs GetFileTreeCommand from the project root and returns the root node of the tree.
    private async Task<FileTreeSnapshotNode> GetFileTreeRootAsync(int depth, string glob = "", string typeFilter = "")
    {
        var command = new GetFileTreeCommand(_workspaceWrapper)
        {
            Resource = ResourceKey.Empty,
            Depth = depth,
            Glob = glob,
            TypeFilter = typeFilter
        };

        var result = await command.ExecuteAsync();
        result.IsSuccess.Should().BeTrue();

        var root = command.ResultValue.Root;

        return root;
    }
}
