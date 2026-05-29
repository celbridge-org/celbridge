using Celbridge.Explorer.Services;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Models;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

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

        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

        var updateResult = resourceRegistry.UpdateResourceRegistry();
        updateResult.IsSuccess.Should().BeTrue(updateResult.FirstErrorMessage);

        //
        // Check the scanned resources match the files and folders we created earlier.
        //

        var resources = resourceRegistry.ProjectFolder.Children;
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

        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        var folderStateService = new FolderStateService(workspaceWrapper);
        folderStateService.SetExpanded(FolderNameA, true);

        var updateResult = resourceRegistry.UpdateResourceRegistry();
        updateResult.IsSuccess.Should().BeTrue(updateResult.FirstErrorMessage);

        //
        // Check that the folder resource expanded state is tracked correctly.
        //

        var expandedFoldersOut = folderStateService.ExpandedFolders;
        expandedFoldersOut.Count.Should().Be(1);
        // ExpandedFolders stores resource keys in their canonical (prefixed) string form.
        expandedFoldersOut[0].Should().Be("project:" + FolderNameA);

        var folderResource = (resourceRegistry.ProjectFolder.Children[0] as FolderResource)!;
        var folderPath = resourceRegistry.GetResourceKey(folderResource);
        folderStateService.IsExpanded(folderPath).Should().BeTrue();
    }

    [Test]
    public void ResolveResourcePathReturnsCorrectAbsolutePath()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

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
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

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
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

        var resolveResult = resourceRegistry.ResolveResourcePath(
            ResourceKey.Create($"{FolderNameA}/{FileNameB}"));
        resolveResult.IsSuccess.Should().BeTrue();
        var expectedPath = Path.GetFullPath(
            Path.Combine(_resourceFolderPath, FolderNameA, FileNameB));
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    [Platform("Win", Reason = "Asserts the registry rejects wrong-case keys that the OS would otherwise case-fold to an on-disk file. On case-sensitive filesystems (Linux CI) the wrong-case path simply does not exist, so there is nothing for the registry to reject.")]
    public void ResolveResourcePathRejectsWrongCaseKey_WhenFileExistsOnDisk()
    {
        // Windows is case-insensitive at the filesystem layer (would happily
        // resolve "filea.txt" to the on-disk "FileA.txt"), but the registry
        // tree and the cascade scanner are Ordinal-case-sensitive. To keep
        // the abstraction internally consistent, ResolveResourcePath rejects
        // wrong-case keys whose resolved path resolves to an existing file
        // and names the canonical key in the error message.
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);
        var updateResult = resourceRegistry.UpdateResourceRegistry();
        updateResult.IsSuccess.Should().BeTrue(updateResult.FirstErrorMessage);

        // FileA.txt exists on disk (created in Setup); request it as "filea.txt".
        var wrongCaseKey = ResourceKey.Create(FileNameA.ToLowerInvariant());
        var resolveResult = resourceRegistry.ResolveResourcePath(wrongCaseKey);

        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("does not match the on-disk case");
        resolveResult.FirstErrorMessage.Should().Contain($"project:{FileNameA}");
    }

    [Test]
    public void ResolveResourcePathAcceptsKeyForNonExistentResource()
    {
        // The strict case check only fires when the resolved path exists on
        // disk. Keys for resources that don't yet exist (create flows) pass
        // through unchanged so the file gets created at the case the caller
        // supplied.
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);
        var updateResult = resourceRegistry.UpdateResourceRegistry();
        updateResult.IsSuccess.Should().BeTrue(updateResult.FirstErrorMessage);

        var newKey = ResourceKey.Create("NewResource.json");
        var resolveResult = resourceRegistry.ResolveResourcePath(newKey);

        resolveResult.IsSuccess.Should().BeTrue();
        var expectedPath = Path.GetFullPath(Path.Combine(_resourceFolderPath, "NewResource.json"));
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ResolveResourcePathAcceptsNonExistentPath()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

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
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

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
            var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
            resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

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

    [Test]
    public void ProjectRootHandlerIsRegisteredOnProjectFolderPathSet()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var rootHandlerRegistry = new RootHandlerRegistry();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), rootHandlerRegistry);

        // Before ProjectFolderPath is set, no handler is registered.
        rootHandlerRegistry.RootHandlers.Should().BeEmpty();

        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

        rootHandlerRegistry.RootHandlers.Should().ContainKey(ResourceKey.DefaultRoot);
        var handler = rootHandlerRegistry.RootHandlers[ResourceKey.DefaultRoot];
        handler.RootName.Should().Be(ResourceKey.DefaultRoot);
        handler.BackingLocation.Should().Be(_resourceFolderPath);
        handler.Capabilities.IsWritable.Should().BeTrue();
        handler.Capabilities.IsWatched.Should().BeTrue();
    }

    [Test]
    public void ResolveResourcePathFailsClearlyForUnregisteredRoot()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

        var resolveResult = resourceRegistry.ResolveResourcePath(
            ResourceKey.Create("temp:foo/bar"));
        resolveResult.IsFailure.Should().BeTrue();
        resolveResult.FirstErrorMessage.Should().Contain("'temp'");
        resolveResult.FirstErrorMessage.Should().Contain("not registered");
    }

    [Test]
    public void GetAllFileResourcesScopesToProjectRoot()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), new RootHandlerRegistry());
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);
        resourceRegistry.UpdateResourceRegistry();

        // Default form enumerates the project tree.
        var defaultResults = resourceRegistry.GetAllFileResources();
        defaultResults.Should().NotBeEmpty();

        // Explicit project root produces the same result.
        var explicitProject = resourceRegistry.GetAllFileResources(ResourceKey.DefaultRoot);
        explicitProject.Count.Should().Be(defaultResults.Count);

        // Other roots return empty because the registry indexes only the project
        // tree; temp and logs are reachable through their root handlers, not here.
        resourceRegistry.GetAllFileResources("temp").Should().BeEmpty();
    }

    [Test]
    public void RegisterRootHandlerReplacesExistingHandler()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var rootHandlerRegistry = new RootHandlerRegistry();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), rootHandlerRegistry);
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

        var originalHandler = rootHandlerRegistry.RootHandlers[ResourceKey.DefaultRoot];

        // Setting the path again replaces the handler with a new instance for the new path.
        var alternatePath = Path.Combine(
            Path.GetTempPath(), $"Celbridge/{nameof(ResourceRegistryTests)}_alt");
        Directory.CreateDirectory(alternatePath);

        try
        {
            resourceRegistry.InitializeProjectRoot(alternatePath);
            var newHandler = rootHandlerRegistry.RootHandlers[ResourceKey.DefaultRoot];
            newHandler.Should().NotBeSameAs(originalHandler);
            newHandler.BackingLocation.Should().Be(alternatePath);
        }
        finally
        {
            if (Directory.Exists(alternatePath))
            {
                Directory.Delete(alternatePath, true);
            }
        }
    }

    [Test]
    public void GetResourceKeyFromPathDispatchesToLongestPrefixRoot()
    {
        Guard.IsNotNull(_resourceFolderPath);

        var messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        var rootHandlerRegistry = new RootHandlerRegistry();
        var resourceRegistry = new ResourceRegistry(Substitute.For<ILogger<ResourceRegistry>>(), messengerService, new ProjectTreeBuilder(fileIconService), ResourceClassifierTestHelper.BuildEmptyStub(), rootHandlerRegistry);
        resourceRegistry.InitializeProjectRoot(_resourceFolderPath);

        // Register a temp root whose backing folder is nested inside the project folder.
        // A path under the nested folder should match the temp root (longer prefix),
        // not the project root (shorter prefix).
        var tempBacking = Path.Combine(_resourceFolderPath, ".celbridge", "temp");
        Directory.CreateDirectory(tempBacking);

        rootHandlerRegistry.RegisterRootHandler(new TempRootHandler(tempBacking));

        // A path under the project tree but outside .celbridge/temp/ goes to project.
        var projectFilePath = Path.Combine(_resourceFolderPath, FileNameA);
        var projectKeyResult = resourceRegistry.GetResourceKey(projectFilePath);
        projectKeyResult.IsSuccess.Should().BeTrue();
        projectKeyResult.Value.Root.Should().Be(ResourceKey.DefaultRoot);
        projectKeyResult.Value.Path.Should().Be(FileNameA);

        // A path under .celbridge/temp/ dispatches to the temp handler.
        var tempFilePath = Path.Combine(tempBacking, "staging", "foo.txt");
        var tempKeyResult = resourceRegistry.GetResourceKey(tempFilePath);
        tempKeyResult.IsSuccess.Should().BeTrue();
        tempKeyResult.Value.Root.Should().Be("temp");
        tempKeyResult.Value.Path.Should().Be("staging/foo.txt");

        // A path outside any registered root fails clearly.
        var outsidePath = Path.Combine(Path.GetTempPath(), "somewhere_else", "file.txt");
        var outsideKeyResult = resourceRegistry.GetResourceKey(outsidePath);
        outsideKeyResult.IsFailure.Should().BeTrue();
        outsideKeyResult.FirstErrorMessage.Should().Contain("not under any registered resource root");
    }
}
