using Celbridge.Browser.Helpers;
using Celbridge.Resources;
using Celbridge.Tests.Browser;
using Celbridge.Tests.Localization;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Tests WorkshopService, which writes the Workshop document under the temp root. The tests parse the written file
/// back to check the document's one bookmark.
/// </summary>
[TestFixture]
public class WorkshopServiceTests
{
    private const string LocalizedPrefix = "localized:";

    private static readonly ResourceKey WorkshopResource = new("temp:workshop.browser");

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
        _resourceFileSystem
            .SetAttributesAsync(Arg.Any<ResourceKey>(), Arg.Any<FileSystemAttributes>(), Arg.Any<bool>())
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

    private WorkshopService CreateService()
    {
        return new WorkshopService(
            Substitute.For<ILogger<WorkshopService>>(),
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

        service.DocumentResource.Should().Be(WorkshopResource);
    }

    [Test]
    public async Task SeedDocumentAsync_WritesOneBookmarkThatOpensOnStart()
    {
        var service = CreateService();

        await service.SeedDocumentAsync();

        await _resourceFileSystem.Received(1).WriteAllTextAsync(
            WorkshopResource,
            Arg.Any<string>());

        var bookmark = ReadWrittenDocument().Bookmarks.Should().ContainSingle().Which;
        bookmark.Url.Should().Be(CelbridgeUrls.Workshop);
        bookmark.OpenOnStart.Should().BeTrue();
    }

    [Test]
    public async Task SeedDocumentAsync_WritesTheFileThenMakesItReadOnly()
    {
        var service = CreateService();

        await service.SeedDocumentAsync();

        Received.InOrder(() =>
        {
            _resourceFileSystem.WriteAllTextAsync(WorkshopResource, Arg.Any<string>());
            _resourceFileSystem.SetAttributesAsync(WorkshopResource, FileSystemAttributes.ReadOnly, true);
        });
        await _resourceFileSystem.Received(1).SetAttributesAsync(
            Arg.Any<ResourceKey>(),
            Arg.Any<FileSystemAttributes>(),
            Arg.Any<bool>());
    }

    [Test]
    public async Task SeedDocumentAsync_WritesTheLocalizedNameOfTheBookmark()
    {
        // Check the bookmark's name key against the application's strings. A missing key would put the raw key in the
        // document as the bookmark's label.
        var strings = TestLocalizerService.LoadStrings();

        var service = CreateService();

        await service.SeedDocumentAsync();

        var bookmark = ReadWrittenDocument().Bookmarks.Should().ContainSingle().Which;

        // The service resolves the name when it writes the file, so the file carries the localized text.
        bookmark.Name.Should().StartWith(LocalizedPrefix);

        var nameKey = bookmark.Name.Substring(LocalizedPrefix.Length);
        strings.Should().ContainKey(nameKey);
    }

    [Test]
    public async Task SeedDocumentAsync_GivesTheBookmarkTheCelbridgeIcon()
    {
        var service = CreateService();

        await service.SeedDocumentAsync();

        // The parse drops an icon unless the icon is a PNG within the size limit. An icon in the parsed file therefore
        // passed that check. A browser document also drops an icon that fails to decode, so the test decodes the icon.
        var icon = ReadWrittenDocument().Bookmarks.Should().ContainSingle().Which.Favicon;

        icon.Should().NotBeNull();
        FaviconEncoder.CanDecode(icon!).Should().BeTrue();
        TestFavicons.SizeOf(icon!).Should().Be((32, 32));
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

        await _resourceFileSystem.Received(1).WriteAllTextAsync(WorkshopResource, Arg.Any<string>());
    }
}
