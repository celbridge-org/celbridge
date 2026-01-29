using Celbridge.Explorer.Services;
using Celbridge.Messaging.Services;
using Celbridge.Resources.Models;
using Celbridge.Resources.Services;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests;

[TestFixture]
public class ResourceRegistryTests
{
    private const string FolderNameA = "FolderA";
    private const string FileNameA = "FileA.txt";
    private const string FileNameB = "FileB.txt";

    private const string FileContents = "Lorem Ipsum";


    private string? _resourceFolderPath;

    [SetUp]
    public void Setup()
    {
        _resourceFolderPath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ResourceRegistryTests)}");
        if (Directory.Exists(_resourceFolderPath))
        {
            Directory.Delete(_resourceFolderPath, true);
        }

        //
        // Create some files and folders on disk
        //

        Directory.CreateDirectory(_resourceFolderPath);
        Directory.Exists(_resourceFolderPath).Should().BeTrue();

        var filePathA = Path.Combine(_resourceFolderPath, FileNameA);
        File.WriteAllText(filePathA, FileContents);
        File.Exists(filePathA).Should().BeTrue();

        var folderPathA = Path.Combine(_resourceFolderPath, FolderNameA);
        Directory.CreateDirectory(folderPathA);
        Directory.Exists(folderPathA).Should().BeTrue();

        var filePathB = Path.Combine(_resourceFolderPath, FolderNameA, FileNameB);
        File.WriteAllText(filePathB, FileContents);
        File.Exists(filePathB).Should().BeTrue();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_resourceFolderPath))
        {
            Directory.Delete(_resourceFolderPath!, true);
        }
    }

    [Test]
    public void ICanUpdateTheResourceTree()
    {
        Guard.IsNotNull(_resourceFolderPath);

        //
        // Populate the resource tree by scanning the files and folders.
        //

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();

        var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        var updateResult = resourceRegistry.UpdateResourceRegistry();
        updateResult.IsSuccess.Should().BeTrue();

        //
        // Check the scanned resources match the files and folders we created earlier.
        //

        var resources = resourceRegistry.RootFolder.Children;
        resources.Count.Should().Be(2);

        (resources[0] is FolderResource).Should().BeTrue();
        resources[0].Name.Should().Be(FolderNameA);

        (resources[1] is FileResource).Should().BeTrue();
        resources[1].Name.Should().Be(FileNameA);

        var subFolderResource = resources[0] as FolderResource;
        Guard.IsNotNull(subFolderResource);

        subFolderResource.Children.Count.Should().Be(1);
        subFolderResource.Children[0].Name.Should().Be(FileNameB);
    }

    [Test]
    public void ICanExpandAFolderResource()
    {
        Guard.IsNotNull(_resourceFolderPath);

        //
        // Populate the resource tree by scanning the files and folders.
        // Set the folder to be expanded before populating the resource tree.
        //

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();

        var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        var folderStateService = new FolderStateService(workspaceWrapper);
        folderStateService.SetExpanded(FolderNameA, true);

        var updateResult = resourceRegistry.UpdateResourceRegistry();
        updateResult.IsSuccess.Should().BeTrue();

        //
        // Check that the folder resource expanded state is tracked correctly.
        //

        var expandedFoldersOut = folderStateService.ExpandedFolders;
        expandedFoldersOut.Count.Should().Be(1);
        expandedFoldersOut[0].Should().Be(FolderNameA);

        var folderResource = (resourceRegistry.RootFolder.Children[0] as FolderResource)!;
        var folderPath = resourceRegistry.GetResourceKey(folderResource);
        folderStateService.IsExpanded(folderPath).Should().BeTrue();
    }
}
