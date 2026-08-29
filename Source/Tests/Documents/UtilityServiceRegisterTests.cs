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

    private static readonly EditorId NotesItemId = EditorId.Create("acme", "notes");

    private IServiceProvider _serviceProvider = null!;
    private IResourceFileSystem _resourceFileSystem = null!;
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

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.PackageService.Returns(Substitute.For<IPackageService>());
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    // A contribution declaring a workspace item in the given areas. Only a declaration that allows the
    // Utility Panel builds a view, so a document-scoped one can be exercised without a WebView.
    private static ResolvedEditor CreateDeclaredItem(EditorId editorId, params WorkspaceArea[] allowedAreas)
    {
        var descriptor = new UtilityDescriptor
        {
            ResourceExtension = "._notes",
            Icon = "bs-sticky",
            AllowedAreas = allowedAreas,
            DefaultArea = allowedAreas[0]
        };

        var contribution = new EditorContribution
        {
            Id = "notes",
            DisplayName = "Notes",
            UtilityDescriptor = descriptor
        };

        return new ResolvedEditor
        {
            EditorId = editorId,
            Contribution = contribution
        };
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
        return new UtilityRailItem
        {
            ItemId = itemId,
            DisplayName = displayName,
            PanelView = new UtilityRailPanelView(new object(), () => { }, FocusPanelId.Explorer)
        };
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
        projectSettings.Resource!.Resource.Should().Be(ProjectFileResource);
        projectSettings.DisplayName.Should().Be("localized:UtilityPanel_ProjectSettingsTooltip");

        var workshop = railItems.Single(railItem => railItem.ItemId == BuiltInLauncherIds.Workshop);
        workshop.Resource!.Resource.Should().Be(WorkshopResource);
        workshop.DisplayName.Should().Be("localized:UtilityPanel_WorkshopTooltip");

        // A launcher opens a document and never occupies the panel, which is what makes it a launcher.
        workshop.PanelView.Should().BeNull();
        workshop.DefaultArea.Should().Be(WorkspaceArea.Main);
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
    public async Task CreateUtilitiesAsync_DocumentScopedDeclaration_BecomesALauncherWithNoPanelView()
    {
        var service = CreateService();

        var declaredItem = CreateDeclaredItem(NotesItemId, WorkspaceArea.Bottom);
        await service.CreateUtilitiesAsync(new List<ResolvedEditor> { declaredItem });

        var railItem = service.GetRailItems().Single(item => item.ItemId == NotesItemId);

        // Nothing parks a live view in the Utility Panel, so the item has no panel view. That is what makes
        // it a launcher on the rail and what makes its close an ordinary close.
        railItem.PanelView.Should().BeNull();
        railItem.AllowedAreas.Should().Equal(WorkspaceArea.Bottom);
        railItem.DefaultArea.Should().Be(WorkspaceArea.Bottom);

        // It still opens a document, backed by its own seeded file under the utils: root.
        var expectedResource = new ResourceKey($"utils:{NotesItemId}._notes");
        railItem.Resource!.Resource.Should().Be(expectedResource);
        railItem.Resource.Editor.Should().Be(NotesItemId);

        await _resourceFileSystem.Received(1).WriteAllBytesAsync(expectedResource, Arg.Any<byte[]>());

        // No view was built, so there is no live utility to dock.
        service.HasUtility(NotesItemId).Should().BeFalse();
    }

    [Test]
    public async Task FindRailItem_ReportsTheItemPresentingAResource()
    {
        var service = CreateService();

        var declaredItem = CreateDeclaredItem(NotesItemId, WorkspaceArea.Main);
        await service.CreateUtilitiesAsync(new List<ResolvedEditor> { declaredItem });

        var railItem = service.FindRailItem(new ResourceKey($"utils:{NotesItemId}._notes"));

        railItem.Should().NotBeNull();
        railItem!.ItemId.Should().Be(NotesItemId);

        // A resource no rail item presents is not the register's to answer for.
        service.FindRailItem(new ResourceKey("Documents/notes.md")).Should().BeNull();
    }

    [Test]
    public async Task GetItemArea_ReturnsTheDescriptorDefaultForAnItemThatHasNotMoved()
    {
        var service = CreateService();

        service.RegisterBuiltInUtilityItems(new List<UtilityRailItem>
        {
            CreateBuiltInUtilityItem(BuiltInUtilityIds.Explorer, "Explorer")
        });

        await service.CreateUtilitiesAsync(Array.Empty<ResolvedEditor>());

        service.GetItemArea(BuiltInUtilityIds.Explorer).Should().Be(WorkspaceArea.Utility);
        service.GetItemArea(BuiltInLauncherIds.Workshop).Should().Be(WorkspaceArea.Main);

        // An id the register does not hold falls back to the panel rather than claiming a document area.
        service.GetItemArea(EditorId.Create("acme", "absent")).Should().Be(WorkspaceArea.Utility);
    }
}
