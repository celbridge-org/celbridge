using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ListTagsCommand — the engine behind the data_list_tags MCP tool.
/// The command runs the on-demand ResourceScanner across paired sidecars and
/// returns the deduplicated set of tag values, sorted for diff stability.
/// </summary>
[TestFixture]
public class ListTagsCommandTests
{
    private string _projectFolderPath = null!;
    private ResourceRegistry _resourceRegistry = null!;
    private RootHandlerRegistry _rootHandlerRegistry = null!;
    private IMessengerService _messengerService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private ListTagsCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ListTagsCommandTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _messengerService = new MessengerService();
        var fileIconService = new FileIconService();
        _rootHandlerRegistry = new RootHandlerRegistry();
        _resourceRegistry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            _messengerService,
            ProjectTreeBuilderTestHelper.Build(_projectFolderPath, fileIconService),
            ResourceClassifierTestHelper.BuildClassifier(),
            _rootHandlerRegistry,
            TestFileSystem.CreateLocal());
        _resourceRegistry.InitializeProjectRoot(_projectFolderPath);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.RootHandlers.Returns(_rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            _messengerService,
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        var scanner = new ResourceScanner(
            Substitute.For<ILogger<ResourceScanner>>(),
            _workspaceWrapper);
        resourceService.Scanner.Returns(scanner);

        _command = new ListTagsCommand(_workspaceWrapper);
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
    public async Task EmptyWorkspace_ReturnsEmptyList()
    {
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.Should().BeEmpty();
    }

    [Test]
    public async Task ReturnsUniqueTagsAcrossSidecars_SortedOrdinal()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md"), "A.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md"), "B.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "a.md.cel"),
            "_tags = [\"draft\", \"priority:high\"]\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "b.md.cel"),
            "_tags = [\"draft\", \"published\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.Should().Equal(new[]
        {
            "draft",
            "priority:high",
            "published",
        });
    }

    [Test]
    public async Task BrokenSidecars_AreSkipped()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "good.md"), "G.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "good.md.cel"),
            "_tags = [\"keep-me\"]\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "bad.md"), "B.\n");
        File.WriteAllText(Path.Combine(_projectFolderPath, "bad.md.cel"),
            "this is not = valid // toml");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.Should().Equal(new[] { "keep-me" });
    }

    [Test]
    public async Task OrphanSidecars_AreSkipped()
    {
        // No parent file on disk; scanner's sidecar enumeration skips orphans.
        File.WriteAllText(Path.Combine(_projectFolderPath, "orphan.png.cel"),
            "_tags = [\"never-surface\"]\n");
        (await _resourceRegistry.UpdateResourceRegistryAsync()).IsSuccess.Should().BeTrue();

        (await _command.ExecuteAsync()).IsSuccess.Should().BeTrue();

        _command.ResultValue.Should().BeEmpty();
    }
}
