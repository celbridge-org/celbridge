using Celbridge.Project.Resources;
using CommunityToolkit.Diagnostics;
using System.Collections.ObjectModel;

namespace Celbridge.Tests;

[TestFixture]
public class ResourceScannerTests
{
    private string? _resourceFolder;

    [SetUp]
    public void Setup()
    {
        _resourceFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Celbridge/{nameof(ResourceScannerTests)}");
        if (Directory.Exists(_resourceFolder))
        {
            Directory.Delete(_resourceFolder, true);
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_resourceFolder))
        {
            Directory.Delete(_resourceFolder!, true);
        }
    }

    [Test]
    public void ICanScanFileAndFolderResources()
    {
        Guard.IsNotNull(_resourceFolder);

        //
        // Create some files and folders on disk
        //

        Directory.CreateDirectory(_resourceFolder);
        Directory.Exists(_resourceFolder).Should().BeTrue();

        var resourceFileA = System.IO.Path.Combine(_resourceFolder, "ResourceFileA.txt");
        File.WriteAllText(resourceFileA, "ResourceData");
        File.Exists(resourceFileA).Should().BeTrue();

        var resourceFolder = System.IO.Path.Combine(_resourceFolder, "ResourceFolder");
        Directory.CreateDirectory(resourceFolder);
        Directory.Exists(resourceFolder).Should().BeTrue();

        var resourceFileB = System.IO.Path.Combine(_resourceFolder, "ResourceFolder", "ResourceFileB.txt");
        File.WriteAllText(resourceFileB, "ResourceData");
        File.Exists(resourceFileB).Should().BeTrue();

        //
        // Scan the files and folders
        //

        var resourceRegistry = new ResourceRegistry(_resourceFolder);
        var scanResult = resourceRegistry.ScanResources();
        scanResult.IsSuccess.Should().BeTrue();

        //
        // Check the scanned resources match the files we wrote earlier
        //

        var resources = resourceRegistry.Resources;
        resources.Count.Should().Be(2);

        (resources[0] is FolderResource).Should().BeTrue();
        resources[0].Name.Should().Be("ResourceFolder");

        (resources[1] is FileResource).Should().BeTrue();
        resources[1].Name.Should().Be("ResourceFileA.txt");

        var subFolder = resources[0] as FolderResource;
        Guard.IsNotNull(subFolder);

        subFolder.Children.Count.Should().Be(1);
        subFolder.Children[0].Name.Should().Be("ResourceFileB.txt");
    }
}
