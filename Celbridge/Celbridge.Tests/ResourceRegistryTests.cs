using Celbridge.Project.Models;
using Celbridge.Project.Services;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class ResourceRegistryTests
{
    private string? _resourceFolderPath;

    [SetUp]
    public void Setup()
    {
        _resourceFolderPath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ResourceRegistryTests)}");
        if (Directory.Exists(_resourceFolderPath))
        {
            Directory.Delete(_resourceFolderPath, true);
        }
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
    public void ICanScanFileAndFolderResources()
    {
        Guard.IsNotNull(_resourceFolderPath);

        const string FolderNameA = "FolderA";
        const string FileNameA = "FileA.txt";
        const string FileNameB = "FileB.txt";

        const string FileContents = "Lorem Ipsum";

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

        //
        // Scan the files and folders
        //

        var resourceRegistry = new ResourceRegistry(_resourceFolderPath);
        var scanResult = resourceRegistry.UpdateRegistry();
        scanResult.IsSuccess.Should().BeTrue();

        //
        // Check the scanned resources match the files we wrote earlier
        //

        var resources = resourceRegistry.Resources;
        resources.Count.Should().Be(2);

        (resources[0] is FolderResource).Should().BeTrue();
        resources[0].Name.Should().Be(FolderNameA);

        (resources[1] is FileResource).Should().BeTrue();
        resources[1].Name.Should().Be(FileNameA);

        var subFolderResource = resources[0] as FolderResource;
        Guard.IsNotNull(subFolderResource);

        subFolderResource.Children.Count.Should().Be(1);
        subFolderResource.Children[0].Name.Should().Be(FileNameB);

        //
        // Expand a folder and retrieve it from the registry
        //
        var expandedFoldersIn = new List<string>() { FolderNameA };
        resourceRegistry.SetExpandedFolders(expandedFoldersIn);

        var folderResource = (resources[0] as FolderResource)!;
        folderResource.Expanded.Should().BeTrue();

        var expandedFoldersOut = resourceRegistry.GetExpandedFolders();
        expandedFoldersOut.Count.Should().Be(1);
        expandedFoldersOut[0].Should().Be(FolderNameA);

        var folderPath = resourceRegistry.GetResourcePath(folderResource);
        resourceRegistry.IsFolderExpanded(folderPath).Should().BeTrue();
    }
}
