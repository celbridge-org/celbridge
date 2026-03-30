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

    [Test]
    public void ResolveResourcePathReturnsCorrectAbsolutePath()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        var resolveResult = resourceRegistry.ResolveResourcePath(ResourceKey.Create(FileNameA));
        resolveResult.IsSuccess.Should().BeTrue();
        var expectedPath = Path.GetFullPath(Path.Combine(_resourceFolderPath, FileNameA));
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveResourcePathWithEmptyKeyReturnsProjectFolder()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        var resolveResult = resourceRegistry.ResolveResourcePath(ResourceKey.Empty);
        resolveResult.IsSuccess.Should().BeTrue();
        var expectedPath = Path.GetFullPath(_resourceFolderPath);
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveResourcePathWithNestedKeyReturnsCorrectPath()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        var resolveResult = resourceRegistry.ResolveResourcePath(
            ResourceKey.Create($"{FolderNameA}/{FileNameB}"));
        resolveResult.IsSuccess.Should().BeTrue();
        var expectedPath = Path.GetFullPath(
            Path.Combine(_resourceFolderPath, FolderNameA, FileNameB));
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveResourcePathAcceptsNonExistentPath()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        // Non-existent files should still resolve without error
        var resolveResult = resourceRegistry.ResolveResourcePath(
            ResourceKey.Create("nonexistent/file.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().NotBeEmpty();
    }

    [Test]
    public void ResolveResourcePathRoundTripsWithGetResourceKey()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
        resourceRegistry.ProjectFolderPath = _resourceFolderPath;

        var filePath = Path.Combine(_resourceFolderPath, FileNameA);
        var getKeyResult = resourceRegistry.GetResourceKey(filePath);
        getKeyResult.IsSuccess.Should().BeTrue();

        var resolveResult = resourceRegistry.ResolveResourcePath(getKeyResult.Value);
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(Path.GetFullPath(filePath));
    }

    [Test]
    public void ResolveResourcePathRejectsSymlinksWithinProject()
    {
        Guard.IsNotNull(_resourceFolderPath);

        // Create a symlink inside the project folder pointing outside
        var outsideFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(ResourceRegistryTests)}_outside");
        Directory.CreateDirectory(outsideFolder);
        var outsideFile = Path.Combine(outsideFolder, "secret.txt");
        File.WriteAllText(outsideFile, "secret data");

        var symlinkPath = Path.Combine(_resourceFolderPath, "escape_link");

        try
        {
            // Try to create a symlink — this may fail on some systems due to permissions
            Directory.CreateSymbolicLink(symlinkPath, outsideFolder);
        }
        catch (Exception)
        {
            // If symlink creation fails (e.g. insufficient privileges), skip this test
            Assert.Ignore("Cannot create symbolic links on this system");
            return;
        }

        try
        {
            var messengerService = new MessengerService();
            var fileIconService = new FileIconService();
            var resourceRegistry = new ResourceRegistry(messengerService, fileIconService);
            resourceRegistry.ProjectFolderPath = _resourceFolderPath;

            var resolveResult = resourceRegistry.ResolveResourcePath(
                ResourceKey.Create("escape_link/secret.txt"));
            resolveResult.IsFailure.Should().BeTrue();
            resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
            {
                Directory.Delete(symlinkPath);
            }
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }
}
