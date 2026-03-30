using System.Text.Json;
using Celbridge.Explorer;
using Celbridge.Server;
using Celbridge.Tools;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the ExplorerTools MCP tool methods.
/// </summary>
[TestFixture]
public class ExplorerToolTests
{
    private IApplicationServiceProvider _services = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IWorkspaceService _workspaceService = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceService = Substitute.For<IWorkspaceService>();

        _workspaceWrapper.WorkspaceService.Returns(_workspaceService);
        _services.GetRequiredService<IWorkspaceWrapper>().Returns(_workspaceWrapper);
    }

    [Test]
    public void GetContext_ReturnsSelectionAndExpandedFolders()
    {
        var explorerService = Substitute.For<IExplorerService>();
        var folderStateService = Substitute.For<IFolderStateService>();

        var selectedResource = new ResourceKey("src/main.py");
        explorerService.SelectedResource.Returns(selectedResource);
        explorerService.SelectedResources.Returns(new List<ResourceKey> { selectedResource });
        explorerService.FolderStateService.Returns(folderStateService);
        folderStateService.ExpandedFolders.Returns(new List<string> { "src", "src/utils" });

        _workspaceService.ExplorerService.Returns(explorerService);

        var tools = new ExplorerTools(_services);
        var root = ParseResult(tools.GetContext());

        root.GetProperty("selectedResource").GetString().Should().Be("src/main.py");

        var selectedResources = root.GetProperty("selectedResources");
        selectedResources.GetArrayLength().Should().Be(1);
        selectedResources[0].GetString().Should().Be("src/main.py");

        var expandedFolders = root.GetProperty("expandedFolders");
        expandedFolders.GetArrayLength().Should().Be(2);
        expandedFolders[0].GetString().Should().Be("src");
        expandedFolders[1].GetString().Should().Be("src/utils");
    }

    [Test]
    public void GetContext_NoSelectionNoExpandedFolders()
    {
        var explorerService = Substitute.For<IExplorerService>();
        var folderStateService = Substitute.For<IFolderStateService>();

        explorerService.SelectedResource.Returns(ResourceKey.Empty);
        explorerService.SelectedResources.Returns(new List<ResourceKey>());
        explorerService.FolderStateService.Returns(folderStateService);
        folderStateService.ExpandedFolders.Returns(new List<string>());

        _workspaceService.ExplorerService.Returns(explorerService);

        var tools = new ExplorerTools(_services);
        var root = ParseResult(tools.GetContext());

        root.GetProperty("selectedResource").GetString().Should().BeEmpty();
        root.GetProperty("selectedResources").GetArrayLength().Should().Be(0);
        root.GetProperty("expandedFolders").GetArrayLength().Should().Be(0);
    }

    [Test]
    public void GetContext_MultiSelect()
    {
        var explorerService = Substitute.For<IExplorerService>();
        var folderStateService = Substitute.For<IFolderStateService>();

        var anchorResource = new ResourceKey("src/a.py");
        var secondResource = new ResourceKey("src/b.py");
        explorerService.SelectedResource.Returns(anchorResource);
        explorerService.SelectedResources.Returns(new List<ResourceKey> { anchorResource, secondResource });
        explorerService.FolderStateService.Returns(folderStateService);
        folderStateService.ExpandedFolders.Returns(new List<string> { "src" });

        _workspaceService.ExplorerService.Returns(explorerService);

        var tools = new ExplorerTools(_services);
        var root = ParseResult(tools.GetContext());

        root.GetProperty("selectedResource").GetString().Should().Be("src/a.py");
        root.GetProperty("selectedResources").GetArrayLength().Should().Be(2);
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
