using Celbridge.Community;
using Celbridge.Resources;
using Celbridge.Tests.Localization;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Verifies the file from CommunityService: the file's location, and that the browser editor can parse the file back
/// to one navigable bookmark per section.
/// </summary>
[TestFixture]
public class CommunityServiceTests
{
    private const string LocalizedPrefix = "localized:";

    private static readonly ResourceKey CommunityResource = new("temp:community.browser");

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
            return new LocalizedString(key, $"{LocalizedPrefix}{key}");
        });
    }

    private CommunityService CreateService()
    {
        return new CommunityService(
            Substitute.For<ILogger<CommunityService>>(),
            _stringLocalizer,
            _workspaceWrapper);
    }

    // Parses back what the service wrote, which is what the .browser editor reads at open time.
    private BrowserFileContent ReadWrittenDocument()
    {
        var parseResult = BrowserFileContent.TryParse(_writtenContent);
        parseResult.IsSuccess.Should().BeTrue();

        return parseResult.Value;
    }

    [Test]
    public void DocumentResource_PutsTheDocumentUnderTheTempRoot()
    {
        var service = CreateService();

        service.DocumentResource.Should().Be(CommunityResource);
    }

    [Test]
    public async Task SeedDocumentAsync_WritesADocumentThatOpensOnTheLandingPage()
    {
        var service = CreateService();

        await service.SeedDocumentAsync();

        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            CommunityResource,
            Arg.Any<string>());

        var openOnStart = ReadWrittenDocument().Bookmarks.Where(bookmark => bookmark.OpenOnStart);
        openOnStart.Should().ContainSingle()
            .Which.Url.Should().Be(CommunityUrls.Celbridge);
    }

    [Test]
    public async Task SeedDocumentAsync_BookmarksEverySectionOfTheSite()
    {
        var service = CreateService();

        await service.SeedDocumentAsync();

        // The landing page is bookmarked too, so the bookmark list alone is a complete way around the site.
        var urls = ReadWrittenDocument().Bookmarks.Select(bookmark => bookmark.Url);

        urls.Should().Equal(CommunityUrls.Celbridge, CommunityUrls.Learn, CommunityUrls.Forum);
    }

    [Test]
    public async Task SeedDocumentAsync_WritesTheLocalizedNameOfEveryBookmark()
    {
        // A key with no entry bakes the raw key into the document as the bookmark's label, so the keys the
        // service reads are checked against the strings the application actually ships.
        var strings = TestLocalizerService.LoadStrings();

        var service = CreateService();

        await service.SeedDocumentAsync();

        foreach (var bookmark in ReadWrittenDocument().Bookmarks)
        {
            // The name is resolved at seed time, so the file carries localized text rather than the key.
            bookmark.Name.Should().StartWith(LocalizedPrefix);

            var nameKey = bookmark.Name.Substring(LocalizedPrefix.Length);
            strings.Should().ContainKey(nameKey);
        }
    }

    [Test]
    public async Task SeedDocumentAsync_WritesNothingWhenNoWorkspaceIsLoaded()
    {
        // Seeding happens mid-load, so this guards on the workspace service rather than on the load having
        // finished, which is still false at that point.
        _workspaceWrapper.HasWorkspaceService.Returns(false);

        var service = CreateService();

        await service.SeedDocumentAsync();

        await _resourceFileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<ResourceKey>(), Arg.Any<string>());
    }

    [Test]
    public async Task SeedDocumentAsync_WritesWhileTheWorkspaceViewIsStillLoading()
    {
        // The seed runs partway through the workspace load, when the workspace service exists but the load
        // has not finished.
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var service = CreateService();

        await service.SeedDocumentAsync();

        await _resourceFileSystem.Received(1).WriteAllTextAsync(CommunityResource, Arg.Any<string>());
    }
}
