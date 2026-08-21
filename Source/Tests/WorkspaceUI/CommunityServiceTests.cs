using Celbridge.Community;
using Celbridge.Resources;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Verifies the document CommunityService writes for a link: where it lands, and that the .webview editor can
/// parse it back to the link's page.
/// </summary>
[TestFixture]
public class CommunityServiceTests
{
    private static readonly CommunityLink TestLink = new()
    {
        LinkId = "forum",
        DocumentName = "forum",
        Url = "https://example.com/forum",
        TooltipKey = "UtilityPanel_ForumTooltip"
    };

    private IResourceFileSystem _resourceFileSystem = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private string _writtenContent = string.Empty;

    [SetUp]
    public void Setup()
    {
        _writtenContent = string.Empty;

        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        _resourceFileSystem
            .WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Do<string>(content => _writtenContent = content))
            .Returns(Result.Ok());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.FileSystem.Returns(_resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.HasWorkspaceService.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    private CommunityService CreateService()
    {
        return new CommunityService(
            Substitute.For<ILogger<CommunityService>>(),
            _workspaceWrapper);
    }

    [Test]
    public void GetLinkResource_PutsTheDocumentUnderTheTempRoot()
    {
        var service = CreateService();

        var resource = service.GetLinkResource(TestLink);

        resource.Should().Be(new ResourceKey("temp:forum.webview"));
    }

    [Test]
    public async Task WriteLinkDocumentAsync_WritesADocumentThatOpensTheLinkPage()
    {
        var service = CreateService();

        var result = await service.WriteLinkDocumentAsync(TestLink);

        result.IsSuccess.Should().BeTrue();

        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            new ResourceKey("temp:forum.webview"),
            Arg.Any<string>());

        var parseResult = WebViewFileContent.TryParse(_writtenContent);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.SourceUrl.Should().Be("https://example.com/forum");
    }

    [Test]
    public async Task WriteLinkDocumentAsync_FailsWhenNoWorkspaceIsLoaded()
    {
        // Seeding happens mid-load, so this guards on the workspace service rather than on the load having
        // finished, which is still false at that point.
        _workspaceWrapper.HasWorkspaceService.Returns(false);

        var service = CreateService();

        var result = await service.WriteLinkDocumentAsync(TestLink);

        result.IsFailure.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task WriteLinkDocumentAsync_SucceedsWhileTheWorkspaceViewIsStillLoading()
    {
        // The seed runs partway through the workspace load, when the workspace service exists but the load
        // has not finished. Guarding on the load instead dropped every seed, and the link's document was
        // then missing when the layout restore looked for it.
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var service = CreateService();

        var result = await service.WriteLinkDocumentAsync(TestLink);

        result.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            new ResourceKey("temp:forum.webview"),
            Arg.Any<string>());
    }

    [Test]
    public void FindLink_ReturnsNullForAnUnknownId()
    {
        var service = CreateService();

        service.FindLink("no-such-link").Should().BeNull();
        service.FindLink(CommunityLinks.Forum.LinkId).Should().Be(CommunityLinks.Forum);
    }
}
