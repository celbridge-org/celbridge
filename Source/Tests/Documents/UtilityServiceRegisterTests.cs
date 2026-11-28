using Celbridge.Commands;
using Celbridge.Documents.Services;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workshop;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers the rail register UtilityService assembles: the order it holds, the shortcuts it builds, and the
/// area it reports for an item that has never moved. Creating a contribution utility instantiates a WebView,
/// so these exercise a workspace that declares none.
/// </summary>
[TestFixture]
public class UtilityServiceRegisterTests
{
    private static readonly ResourceKey ProjectFileResource = new("Acme.celbridge");
    private static readonly ResourceKey WorkshopResource = new("temp:workshop.browser");

    private IServiceProvider _serviceProvider = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
    private IDocumentsService _documentsService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        var project = Substitute.For<IProject>();
        project.ProjectFileResource.Returns(ProjectFileResource);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var workshopService = Substitute.For<IWorkshopService>();
        workshopService.DocumentResource.Returns(WorkshopResource);

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
        _serviceProvider.GetService(typeof(IWorkshopService)).Returns(workshopService);
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
        var safeModeService = TestSafeMode.Create(isActive: false);
        workspaceService.SafeModeService.Returns(safeModeService);
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
            BuiltInShortcutIds.Workshop);
    }

    [Test]
    public async Task GetRailItems_BandsTheItemsByWhereTheyCameFrom()
    {
        // A project that declares a shortcut of its own, which is the band the built-in shortcuts must
        // stay out of.
        var project = Substitute.For<IProject>();
        project.ProjectFileResource.Returns(ProjectFileResource);
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
    public async Task CreateUtilitiesAsync_ShortcutWithoutARailButton_BuildsNoButton()
    {
        // A shortcut that only opens its document on load takes no place on the rail.
        var project = Substitute.For<IProject>();
        project.ProjectFileResource.Returns(ProjectFileResource);
        project.Config.Returns(new ProjectConfig
        {
            DocumentShortcuts = new List<DocumentShortcut>
            {
                new() { Resource = "Notes.md" },
                new() { Resource = "Startup.md", HasRailButton = false, OpenOnLoad = true }
            }
        });

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);
        _serviceProvider.GetService(typeof(IProjectService)).Returns(projectService);

        var service = CreateService();

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        var projectItems = service.GetRailItems()
            .Where(railItem => railItem.Group == RailItemGroup.ProjectItem)
            .ToList();

        projectItems.Should().ContainSingle();
        projectItems[0].FileResource.Should().Be(new ResourceKey("Notes.md"));
    }

    [Test]
    public async Task CreateUtilitiesAsync_InSafeMode_BuildsOnlyTheItemsOnTheList()
    {
        // The project has one shortcut, and a package contributes a utility.
        var project = Substitute.For<IProject>();
        project.ProjectFileResource.Returns(ProjectFileResource);
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

        var safeModeService = TestSafeMode.Create(isActive: true);
        _workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        var packageUtility = new ResolvedEditor
        {
            EditorId = new EditorId("acme-tools.widget"),
            Contribution = new EditorContribution
            {
                UtilityDescriptor = new UtilityDescriptor()
            }
        };

        var service = CreateService();

        service.RegisterBuiltInUtilityItems(new List<UtilityRailItem>
        {
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Explorer, "Explorer"),
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Search, "Search")
        });

        await service.CreateUtilitiesAsync(new List<ResolvedEditor> { packageUtility });

        var itemIds = service.GetRailItems().Select(railItem => railItem.ItemId);
        itemIds.Should().Equal(
            BuiltInUtilityIds.Explorer,
            BuiltInUtilityIds.Search,
            BuiltInShortcutIds.ProjectSettings);

        // The package's utility is never created, so its backing file is never seeded.
        service.HasUtility(packageUtility.EditorId).Should().BeFalse();
        await _resourceFileSystem.DidNotReceiveWithAnyArgs().GetInfoAsync(default);
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

        var workshop = railItems.Single(railItem => railItem.ItemId == BuiltInShortcutIds.Workshop);
        workshop.FileResource.Should().Be(WorkshopResource);
        workshop.DisplayName.Should().Be("localized:UtilityPanel_WorkshopTooltip");

        // A document shortcut opens a document and never occupies the panel, which is what makes it one.
        workshop.PanelView.Should().BeNull();
        workshop.DockArea.Should().Be(WorkspaceArea.Main);
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

        // The Workshop's document is closed, so the Workshop occupies no area. The Workshop's declared area applies
        // only when the document opens.
        service.GetCurrentArea(BuiltInShortcutIds.Workshop).Should().BeNull();

        // An id the register does not hold occupies nothing either, rather than claiming the panel.
        service.GetCurrentArea(EditorId.Create("acme", "absent")).Should().BeNull();
    }

    [Test]
    public async Task GetCurrentArea_ReportsWhereAShortcutDocumentActuallyIs()
    {
        var service = CreateService();

        // The user moved the Workshop tab out of the area its declaration names.
        var openDocument = new OpenDocumentInfo(
            WorkshopResource,
            new DocumentAddress(0, DocumentSection.BottomLeft, 0),
            BuiltInEditors.BrowserEditorId);
        _documentsService.FindOpenDocument(WorkshopResource).Returns(openDocument);

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        service.GetCurrentArea(BuiltInShortcutIds.Workshop).Should().Be(WorkspaceArea.Bottom);
    }
}
