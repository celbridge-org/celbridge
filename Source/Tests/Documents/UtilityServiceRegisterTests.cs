using Celbridge.Commands;
using Celbridge.Documents.Services;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Community;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers the rail register UtilityService assembles: the order it holds, the shortcuts it builds, and the
/// area it reports for an item that has never moved. Creating a contribution utility instantiates a WebView,
/// so these exercise a workspace that declares none.
/// </summary>
[TestFixture]
public class UtilityServiceRegisterTests
{
    private const string ProjectFilePath = "C:/Projects/Acme/Acme.celbridge";

    private static readonly ResourceKey ProjectFileResource = new("Acme.celbridge");
    private static readonly ResourceKey CommunityResource = new("temp:community.webview");

    private IServiceProvider _serviceProvider = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private IDocumentsService _documentsService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        var project = Substitute.For<IProject>();
        project.ProjectFilePath.Returns(ProjectFilePath);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var communityService = Substitute.For<ICommunityService>();
        communityService.DocumentResource.Returns(CommunityResource);

        // IStringLocalizer.GetString(string) is an extension method over the indexer, so the indexer is what
        // NSubstitute can stub. Each key echoes itself, so a test can tell which was read.
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var key = (string)callInfo[0];
            return new LocalizedString(key, $"localized:{key}");
        });

        _serviceProvider = Substitute.For<IServiceProvider>();
        _serviceProvider.GetService(typeof(IProjectService)).Returns(projectService);
        _serviceProvider.GetService(typeof(ICommunityService)).Returns(communityService);
        _serviceProvider.GetService(typeof(IStringLocalizer)).Returns(stringLocalizer);
        _serviceProvider.GetService(typeof(IIconService)).Returns(new IconService());
        // Resolve() reads this dictionary directly, so it has to be a real one rather than a default null.
        var packageLocalizationService = Substitute.For<IPackageLocalizationService>();
        packageLocalizationService.LoadStrings(Arg.Any<PackageInfo>(), Arg.Any<string?>())
            .Returns(new Dictionary<string, string>());
        _serviceProvider.GetService(typeof(IPackageLocalizationService)).Returns(packageLocalizationService);
        _serviceProvider.GetService(typeof(ILogger<UtilityResourceSeeder>)).Returns(Substitute.For<ILogger<UtilityResourceSeeder>>());

        // The service refuses to be built once a workspace is loaded: only the workspace service may create it.
        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        // Seeding a declared item's backing file runs through the workspace's file system. Report the file
        // as absent so the seed writes, and accept the write.
        _resourceFileSystem = Substitute.For<IResourceFileSystem>();
        _resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Result<StorageItemInfo>.Fail("not found"));
        _resourceFileSystem.WriteAllBytesAsync(Arg.Any<ResourceKey>(), Arg.Any<byte[]>())
            .Returns(Result.Ok());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.FileSystem.Returns(_resourceFileSystem);

        // GetCurrentArea asks the documents service where a shortcut's tab actually is. Nothing is open by
        // default, so each test that cares opens one.
        _documentsService = Substitute.For<IDocumentsService>();

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.PackageService.Returns(Substitute.For<IPackageService>());
        workspaceService.DocumentsService.Returns(_documentsService);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    private UtilityService CreateService()
    {
        return new UtilityService(
            _serviceProvider,
            Substitute.For<ILogger<UtilityService>>(),
            Substitute.For<IMessengerService>(),
            Substitute.For<ICommandService>(),
            _workspaceWrapper);
    }

    private static UtilityRailItem CreateBuiltInUtilityItem(EditorId itemId, string displayName)
    {
        return UtilityRailItem.CreatePanelUtility(
            RailItemGroup.BuiltInUtility,
            itemId,
            $"{itemId}-utility-button",
            "folder",
            displayName,
            displayName,
            new UtilityRailPanelView(new object(), () => { }, FocusPanelId.Explorer));
    }

    [Test]
    public void GetRailItems_IsEmptyBeforeTheUtilitiesAreCreated()
    {
        var service = CreateService();

        service.GetRailItems().Should().BeEmpty();
    }

    [Test]
    public async Task GetRailItems_HoldsTheRegisteredBuiltInsAheadOfTheShortcuts()
    {
        var service = CreateService();

        service.RegisterBuiltInUtilityItems(new List<UtilityRailItem>
        {
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Explorer, "Explorer"),
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Search, "Search")
        });

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        var railItems = service.GetRailItems();
        var itemIds = railItems.Select(railItem => railItem.ItemId);

        itemIds.Should().Equal(
            BuiltInUtilityIds.Explorer,
            BuiltInUtilityIds.Search,
            BuiltInShortcutIds.ProjectSettings,
            BuiltInShortcutIds.Community);
    }

    [Test]
    public async Task GetRailItems_BandsTheItemsByWhereTheyCameFrom()
    {
        // A project that declares a shortcut of its own, which is the band the built-in shortcuts must
        // stay out of.
        var project = Substitute.For<IProject>();
        project.ProjectFilePath.Returns(ProjectFilePath);
        project.Config.Returns(new ProjectConfig
        {
            DocumentShortcuts = new List<DocumentShortcut>
            {
                new() { Resource = "Notes.md" }
            }
        });

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);
        _serviceProvider.GetService(typeof(IProjectService)).Returns(projectService);

        var service = CreateService();

        service.RegisterBuiltInUtilityItems(new List<UtilityRailItem>
        {
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Explorer, "Explorer")
        });

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        // The rail draws a gap between bands, so a shortcut the project declares bands with the project's
        // own utilities rather than with the built-in shortcuts pinned at the end.
        var groups = service.GetRailItems().Select(railItem => railItem.Group);

        groups.Should().Equal(
            RailItemGroup.BuiltInUtility,
            RailItemGroup.ProjectItem,
            RailItemGroup.BuiltInShortcut,
            RailItemGroup.BuiltInShortcut);
    }

    [Test]
    public async Task CreateUtilitiesAsync_BuildsTheShortcutsWithTheirResources()
    {
        var service = CreateService();

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        var railItems = service.GetRailItems();

        var projectSettings = railItems.Single(railItem => railItem.ItemId == BuiltInShortcutIds.ProjectSettings);
        projectSettings.FileResource.Should().Be(ProjectFileResource);
        projectSettings.DisplayName.Should().Be("localized:UtilityPanel_ProjectSettingsTooltip");

        var community = railItems.Single(railItem => railItem.ItemId == BuiltInShortcutIds.Community);
        community.FileResource.Should().Be(CommunityResource);
        community.DisplayName.Should().Be("localized:UtilityPanel_CommunityTooltip");

        // A document shortcut opens a document and never occupies the panel, which is what makes it one.
        community.PanelView.Should().BeNull();
        community.DockArea.Should().Be(WorkspaceArea.Main);
    }

    [Test]
    public async Task CreateUtilitiesAsync_NoProjectLoaded_OmitsTheProjectSettingsShortcut()
    {
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _serviceProvider.GetService(typeof(IProjectService)).Returns(projectService);

        var service = CreateService();

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        var itemIds = service.GetRailItems().Select(railItem => railItem.ItemId);
        itemIds.Should().NotContain(BuiltInShortcutIds.ProjectSettings);
    }

    [Test]
    public async Task GetCurrentArea_ReportsThePanelForAUtilityAndNothingForAClosedShortcut()
    {
        var service = CreateService();

        service.RegisterBuiltInUtilityItems(new List<UtilityRailItem>
        {
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Explorer, "Explorer")
        });

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        // A utility always occupies an area, whether or not it has a live view yet.
        service.GetCurrentArea(BuiltInUtilityIds.Explorer).Should().Be(WorkspaceArea.Utility);

        // No document is open for the Community, so it occupies nothing. Its declared area says where it
        // would open, which is a different question.
        service.GetCurrentArea(BuiltInShortcutIds.Community).Should().BeNull();

        // An id the register does not hold occupies nothing either, rather than claiming the panel.
        service.GetCurrentArea(EditorId.Create("acme", "absent")).Should().BeNull();
    }

    [Test]
    public async Task GetCurrentArea_ReportsWhereAShortcutDocumentActuallyIs()
    {
        var service = CreateService();

        // The user moved the Community tab out of the area its declaration names.
        var openDocument = new OpenDocumentInfo(
            CommunityResource,
            new DocumentAddress(0, DocumentSection.BottomLeft, 0),
            BuiltInEditors.WebViewEditorId);
        _documentsService.FindOpenDocument(CommunityResource).Returns(openDocument);

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        service.GetCurrentArea(BuiltInShortcutIds.Community).Should().Be(WorkspaceArea.Bottom);
    }
}
