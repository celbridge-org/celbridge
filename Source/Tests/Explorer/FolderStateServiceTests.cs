using Celbridge.Explorer.Services;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Tests loading the expanded folders the workspace saved, against a real project folder. The load keeps only the
/// saved folders that are still folders on disk.
/// </summary>
[TestFixture]
public class FolderStateServiceTests
{
    private string _projectFolderPath = null!;
    private IWorkspacePropertyBag _propertyBag = null!;
    private FolderStateService _folderStateService = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(FolderStateServiceTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(_projectFolderPath));
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _propertyBag = Substitute.For<IWorkspacePropertyBag>();
        var workspaceSettings = Substitute.For<IWorkspaceSettingsService>();
        workspaceSettings.PropertyBag.Returns(_propertyBag);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.WorkspaceSettings.Returns(workspaceSettings);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        _folderStateService = new FolderStateService(workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task Load_LeavesOutTheSavedFoldersThatAreGoneFromDisk()
    {
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "kept", "inner"));
        File.WriteAllText(Path.Combine(_projectFolderPath, "notes.txt"), "notes");
        SaveExpandedFolders("project:kept", "project:kept/inner", "project:gone", "project:gone/inner", "project:notes.txt");

        await _folderStateService.LoadAsync();

        _folderStateService.ExpandedFolders.Order().Should().Equal(new ResourceKey("kept"), new ResourceKey("kept/inner"));
    }

    [Test]
    public async Task Load_LeavesOutASavedEntryThatIsNotAResourceKey()
    {
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "kept"));
        SaveExpandedFolders("project:kept", "/not a key");

        await _folderStateService.LoadAsync();

        _folderStateService.ExpandedFolders.Should().Equal(new ResourceKey("kept"));
    }

    private void SaveExpandedFolders(params string[] expandedFolders)
    {
        _propertyBag.GetPropertyAsync<List<string>>("ExpandedFolders")
            .Returns(Task.FromResult<List<string>?>(expandedFolders.ToList()));
    }
}
