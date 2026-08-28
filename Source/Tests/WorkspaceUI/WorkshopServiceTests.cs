using Celbridge.Resources;
using Celbridge.WebHost;
using Celbridge.Workshop;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Verifies the document WorkshopService writes: where it lands, and that the .webview editor can parse it
/// back to a landing page and one navigable bookmark per section.
/// </summary>
[TestFixture]
public class WorkshopServiceTests
{
    private static readonly ResourceKey WorkshopResource = new("temp:workshop.webview");

    private IResourceFileSystem _resourceFileSystem = null!;
    private IStringLocalizer _stringLocalizer = null!;
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

        // IStringLocalizer.GetString(string) is an extension method that calls the indexer at runtime, so the
        // indexer is what NSubstitute can stub. Each key echoes itself, so a test can tell which was read.
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var key = (string)callInfo[0];
            return new LocalizedString(key, $"localized:{key}");
        });
    }

    private WorkshopService CreateService()
    {
        return new WorkshopService(
            Substitute.For<ILogger<WorkshopService>>(),
            _stringLocalizer,
            _workspaceWrapper);
    }

    [Test]
    public void DocumentResource_PutsTheDocumentUnderTheTempRoot()
    {
        var service = CreateService();

        service.DocumentResource.Should().Be(WorkshopResource);
    }

    [Test]
    public async Task WriteDocumentAsync_WritesADocumentThatOpensOnTheLandingPage()
    {
        var service = CreateService();

        var result = await service.WriteDocumentAsync();

        result.IsSuccess.Should().BeTrue();

        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            WorkshopResource,
            Arg.Any<string>());

        var parseResult = WebViewFileContent.TryParse(_writtenContent);
        parseResult.IsSuccess.Should().BeTrue();

        var content = parseResult.Value;
        content.SourceUrl.Should().Be(WorkshopSections.Celbridge.Url);
        content.ShowUrlBar.Should().BeTrue();
        content.ShowBookmarksBar.Should().BeTrue();
    }

    [Test]
    public async Task WriteDocumentAsync_WritesOneNavigableBookmarkPerSection()
    {
        var service = CreateService();

        await service.WriteDocumentAsync();

        var parseResult = WebViewFileContent.TryParse(_writtenContent);
        parseResult.IsSuccess.Should().BeTrue();

        var bookmarks = parseResult.Value.Bookmarks;
        bookmarks.Should().HaveCount(WorkshopSections.All.Count);

        for (int i = 0; i < WorkshopSections.All.Count; i++)
        {
            var section = WorkshopSections.All[i];
            var bookmark = bookmarks[i];

            bookmark.Url.Should().Be(section.Url);
            bookmark.Icon.Should().Be(section.IconName);

            // The name is resolved at seed time, so the file carries the localized text rather than the key.
            bookmark.Name.Should().Be($"localized:{section.NameKey}");
        }
    }

    [Test]
    public async Task WriteDocumentAsync_FailsWhenNoWorkspaceIsLoaded()
    {
        // Seeding happens mid-load, so this guards on the workspace service rather than on the load having
        // finished, which is still false at that point.
        _workspaceWrapper.HasWorkspaceService.Returns(false);

        var service = CreateService();

        var result = await service.WriteDocumentAsync();

        result.IsFailure.Should().BeTrue();
        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task WriteDocumentAsync_SucceedsWhileTheWorkspaceViewIsStillLoading()
    {
        // The seed runs partway through the workspace load, when the workspace service exists but the load
        // has not finished. Guarding on the load instead dropped the seed, and the document was then missing
        // when the layout restore looked for it.
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var service = CreateService();

        var result = await service.WriteDocumentAsync();

        result.IsSuccess.Should().BeTrue();
        await _resourceFileSystem.Received(1).WriteAllTextAsync(WorkshopResource, Arg.Any<string>());
    }
}
