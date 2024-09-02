using Celbridge.Messaging.Services;
using Celbridge.Explorer.Models;
using Celbridge.Explorer.Services;
using Celbridge.UserInterface.Services;
using CommunityToolkit.Diagnostics;

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
        var iconService = new IconService();

        var resourceRegistry = new ResourceRegistry(messengerService, iconService);
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
        var iconService = new IconService();

        var resourceRegistry = new ResourceRegistry(messengerService, iconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        resourceRegistry.SetFolderIsExpanded(FolderNameA, true);

        var updateResult = resourceRegistry.UpdateResourceRegistry();
        updateResult.IsSuccess.Should().BeTrue();

        //
        // Check that the folder resource is expanded.
        //

        var folderResource = (resourceRegistry.RootFolder.Children[0] as FolderResource)!;
        folderResource.IsExpanded.Should().BeTrue();

        var expandedFoldersOut = resourceRegistry.ExpandedFolders;
        expandedFoldersOut.Count.Should().Be(1);
        expandedFoldersOut[0].Should().Be(FolderNameA);

        var folderPath = resourceRegistry.GetResourceKey(folderResource);
        resourceRegistry.IsFolderExpanded(folderPath).Should().BeTrue();
    }
}
