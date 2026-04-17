using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities;
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
    private IWorkspaceWrapper _workspaceWrapper = null!;

    private const string FolderName = "Folder";
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

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        _resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        _resourceRegistry.ProjectFolderPath = _projectFolderPath;
        _resourceRegistry.UpdateResourceRegistry();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, recursive: true);
        }
    }

    // ---- GetFileInfoCommand ----------------------------------------------------------------

    [Test]
    public async Task GetFileInfo_ForTextFile_ReportsTextAndLineCount()
    {
        var textBinarySniffer = new TextBinarySniffer();
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
        var textBinarySniffer = new TextBinarySniffer();
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
        var textBinarySniffer = new TextBinarySniffer();
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

    // ---- ListFolderContentsCommand --------------------------------------------------------

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

    // ---- GetFileTreeCommand ---------------------------------------------------------------

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
}
