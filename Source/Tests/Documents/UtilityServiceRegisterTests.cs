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

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers the rail register UtilityService assembles: the order it holds, the launchers it builds, and the
/// area it reports for an item that has never moved. Creating a contribution utility instantiates a WebView,
/// so these exercise a workspace that declares none.
/// </summary>
[TestFixture]
public class UtilityServiceRegisterTests
{
    private const string ProjectFilePath = "C:/Projects/Acme/Acme.celbridge";

    private static readonly ResourceKey ProjectFileResource = new("Acme.celbridge");
    private static readonly ResourceKey WorkshopResource = new("temp:workshop.webview");

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

        // GetCurrentArea asks the documents service where a launcher's tab actually is. Nothing is open by
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
    public async Task GetRailItems_HoldsTheRegisteredBuiltInsAheadOfTheLaunchers()
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
            BuiltInLauncherIds.ProjectSettings,
            BuiltInLauncherIds.Workshop);
    }

    [Test]
    public async Task CreateUtilitiesAsync_BuildsTheLaunchersWithTheirResources()
    {
        var service = CreateService();

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        var railItems = service.GetRailItems();

        var projectSettings = railItems.Single(railItem => railItem.ItemId == BuiltInLauncherIds.ProjectSettings);
        projectSettings.FileResource.Should().Be(ProjectFileResource);
        projectSettings.DisplayName.Should().Be("localized:UtilityPanel_ProjectSettingsTooltip");

        var workshop = railItems.Single(railItem => railItem.ItemId == BuiltInLauncherIds.Workshop);
        workshop.FileResource.Should().Be(WorkshopResource);
        workshop.DisplayName.Should().Be("localized:UtilityPanel_WorkshopTooltip");

        // A launcher opens a document and never occupies the panel, which is what makes it a launcher.
        workshop.PanelView.Should().BeNull();
        workshop.DockArea.Should().Be(WorkspaceArea.Main);
    }

    [Test]
    public async Task CreateUtilitiesAsync_NoProjectLoaded_OmitsTheProjectSettingsLauncher()
    {
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _serviceProvider.GetService(typeof(IProjectService)).Returns(projectService);

        var service = CreateService();

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        var itemIds = service.GetRailItems().Select(railItem => railItem.ItemId);
        itemIds.Should().NotContain(BuiltInLauncherIds.ProjectSettings);
    }

    [Test]
    public async Task GetCurrentArea_ReportsThePanelForAUtilityAndNothingForAClosedLauncher()
    {
        var service = CreateService();

        service.RegisterBuiltInUtilityItems(new List<UtilityRailItem>
        {
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Explorer, "Explorer")
        });

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        // A utility always occupies an area, whether or not it has a live view yet.
        service.GetCurrentArea(BuiltInUtilityIds.Explorer).Should().Be(WorkspaceArea.Utility);

        // No document is open for the Workshop, so it occupies nothing. Its declared area says where it
        // would open, which is a different question.
        service.GetCurrentArea(BuiltInLauncherIds.Workshop).Should().BeNull();

        // An id the register does not hold occupies nothing either, rather than claiming the panel.
        service.GetCurrentArea(EditorId.Create("acme", "absent")).Should().BeNull();
    }

    [Test]
    public async Task GetCurrentArea_ReportsWhereALauncherDocumentActuallyIs()
    {
        var service = CreateService();

        // The user moved the Workshop tab out of the area its declaration names.
        var openDocument = new OpenDocumentInfo(
            WorkshopResource,
            new DocumentAddress(0, DocumentSection.BottomLeft, 0),
            BuiltInEditors.WebViewEditorId);
        _documentsService.FindOpenDocument(WorkshopResource).Returns(openDocument);

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        service.GetCurrentArea(BuiltInLauncherIds.Workshop).Should().Be(WorkspaceArea.Bottom);
    }
}
