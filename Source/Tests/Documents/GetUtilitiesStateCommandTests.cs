using Celbridge.Documents.Commands;
using Celbridge.Packages;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers GetUtilitiesStateCommand's catalog building: the built-in Explorer and Search surfaces,
/// filtering out non-utility contributions, and the reported dock location and isShown state.
/// </summary>
[TestFixture]
public class GetUtilitiesStateCommandTests
{
    [Test]
    public async Task Execute_ReturnsBuiltInsAndCustomUtilitiesWithShownState()
    {
        var panelUtility = new EditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "widget",
            DisplayName = "Widget Panel",
            UtilityDescriptor = new UtilityDescriptor { ResourceExtension = "._widget" }
        };

        var documentUtility = new EditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "notepad",
            DisplayName = "Notepad",
            UtilityDescriptor = new UtilityDescriptor { ResourceExtension = "._notepad" }
        };

        // A non-utility editor contribution must be filtered out of the catalog.
        var nonUtility = new EditorContribution
        {
            Package = new PackageInfo { Name = "celbridge" },
            Id = "code",
            DisplayName = "Code Editor"
        };

        var resolvedEditors = new List<ResolvedEditor>
        {
            CreateInstance("widget-panel", panelUtility),
            CreateInstance("notepad", documentUtility),
            CreateInstance("code", nonUtility)
        };

        // The widget-panel utility is the active rail surface, so it is the only utility shown.
        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(new EditorId("widget-panel"));

        var packageService = Substitute.For<IPackageService>();
        packageService.GetResolvedEditors().Returns(resolvedEditors);

        // Both declared utilities were created, so both are live and listed.
        var utilityService = Substitute.For<IUtilityService>();
        utilityService.HasUtility(Arg.Any<EditorId>()).Returns(true);

        // No utilities are docked: no documents are open.
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.GetOpenDocuments().Returns(Array.Empty<OpenDocumentInfo>());
        documentsService.ActiveDocument.Returns(ResourceKey.Empty);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(utilityPanel);
        workspaceService.PackageService.Returns(packageService);
        workspaceService.DocumentsService.Returns(documentsService);
        workspaceService.UtilityService.Returns(utilityService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer["UtilityPanel_ExplorerTooltip"].Returns(new LocalizedString("UtilityPanel_ExplorerTooltip", "Explorer"));
        stringLocalizer["UtilityPanel_SearchTooltip"].Returns(new LocalizedString("UtilityPanel_SearchTooltip", "Search"));

        var packageLocalization = Substitute.For<IPackageLocalizationService>();
        packageLocalization.LoadStrings(Arg.Any<PackageInfo>(), Arg.Any<string?>()).Returns(new Dictionary<string, string>());

        var command = new GetUtilitiesStateCommand(workspaceWrapper, stringLocalizer, packageLocalization);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var utilities = command.ResultValue.Utilities;
        utilities.Should().HaveCount(4);

        utilities[0].UtilityId.Should().Be(BuiltInUtilityIds.Explorer);
        utilities[0].DisplayName.Should().Be("Explorer");
        utilities[0].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[0].IsShown.Should().BeFalse();

        utilities[1].UtilityId.Should().Be(BuiltInUtilityIds.Search);
        utilities[1].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[1].IsShown.Should().BeFalse();

        utilities[2].UtilityId.Should().Be(new EditorId("widget-panel"));
        utilities[2].DisplayName.Should().Be("Widget Panel");
        utilities[2].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[2].IsShown.Should().BeTrue();

        utilities[3].UtilityId.Should().Be(new EditorId("notepad"));
        utilities[3].DisplayName.Should().Be("Notepad");
        utilities[3].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[3].IsShown.Should().BeFalse();
    }

    [Test]
    public async Task Execute_DockedUtility_ReportsDockedAndShownByActiveDocument()
    {
        var dockedUtility = new EditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "notepad",
            DisplayName = "Notepad",
            UtilityDescriptor = new UtilityDescriptor { ResourceExtension = "._notepad" }
        };

        var resolvedEditors = new List<ResolvedEditor> { CreateInstance("notepad", dockedUtility) };

        // The backing resource is derived from the editor id and the contribution's extension.
        var utilityResource = new ResourceKey("utils:notepad._notepad");

        // The rail is showing Explorer, so the utility is not the active rail surface. It is instead docked
        // as a document tab and is the active document, so it must be reported as docked and shown.
        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);

        var packageService = Substitute.For<IPackageService>();
        packageService.GetResolvedEditors().Returns(resolvedEditors);

        var utilityService = Substitute.For<IUtilityService>();
        utilityService.HasUtility(Arg.Any<EditorId>()).Returns(true);

        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.GetOpenDocuments().Returns(new List<OpenDocumentInfo>
        {
            new(utilityResource, new DocumentAddress(0, 0, 0), new EditorId("notepad"))
        });
        documentsService.ActiveDocument.Returns(utilityResource);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(utilityPanel);
        workspaceService.PackageService.Returns(packageService);
        workspaceService.DocumentsService.Returns(documentsService);
        workspaceService.UtilityService.Returns(utilityService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer["UtilityPanel_ExplorerTooltip"].Returns(new LocalizedString("UtilityPanel_ExplorerTooltip", "Explorer"));
        stringLocalizer["UtilityPanel_SearchTooltip"].Returns(new LocalizedString("UtilityPanel_SearchTooltip", "Search"));

        var packageLocalization = Substitute.For<IPackageLocalizationService>();
        packageLocalization.LoadStrings(Arg.Any<PackageInfo>(), Arg.Any<string?>()).Returns(new Dictionary<string, string>());

        var command = new GetUtilitiesStateCommand(workspaceWrapper, stringLocalizer, packageLocalization);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var notepad = command.ResultValue.Utilities.Single(utility => utility.UtilityId == new EditorId("notepad"));
        notepad.Location.Should().Be(DockLocation.Document);
        notepad.IsShown.Should().BeTrue();
    }

    [Test]
    public async Task Execute_UtilityNotCreated_IsExcludedFromList()
    {
        // A declared utility whose creation failed is not live, so it must not be listed: app_show_utility
        // gates on the same HasUtility check and would refuse it.
        var liveUtility = new EditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "notepad",
            DisplayName = "Notepad",
            UtilityDescriptor = new UtilityDescriptor { ResourceExtension = "._notepad" }
        };

        var deadUtility = new EditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "broken",
            DisplayName = "Broken",
            UtilityDescriptor = new UtilityDescriptor { ResourceExtension = "._broken" }
        };

        var resolvedEditors = new List<ResolvedEditor>
        {
            CreateInstance("notepad", liveUtility),
            CreateInstance("broken", deadUtility)
        };

        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);

        var packageService = Substitute.For<IPackageService>();
        packageService.GetResolvedEditors().Returns(resolvedEditors);

        var utilityService = Substitute.For<IUtilityService>();
        utilityService.HasUtility(new EditorId("notepad")).Returns(true);
        utilityService.HasUtility(new EditorId("broken")).Returns(false);

        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.GetOpenDocuments().Returns(Array.Empty<OpenDocumentInfo>());
        documentsService.ActiveDocument.Returns(ResourceKey.Empty);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(utilityPanel);
        workspaceService.PackageService.Returns(packageService);
        workspaceService.DocumentsService.Returns(documentsService);
        workspaceService.UtilityService.Returns(utilityService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer["UtilityPanel_ExplorerTooltip"].Returns(new LocalizedString("UtilityPanel_ExplorerTooltip", "Explorer"));
        stringLocalizer["UtilityPanel_SearchTooltip"].Returns(new LocalizedString("UtilityPanel_SearchTooltip", "Search"));

        var packageLocalization = Substitute.For<IPackageLocalizationService>();
        packageLocalization.LoadStrings(Arg.Any<PackageInfo>(), Arg.Any<string?>()).Returns(new Dictionary<string, string>());

        var command = new GetUtilitiesStateCommand(workspaceWrapper, stringLocalizer, packageLocalization);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var utilities = command.ResultValue.Utilities;
        utilities.Should().Contain(utility => utility.UtilityId == new EditorId("notepad"));
        utilities.Should().NotContain(utility => utility.UtilityId == new EditorId("broken"));
    }

    private static ResolvedEditor CreateInstance(string editorId, EditorContribution contribution)
    {
        return new ResolvedEditor
        {
            EditorId = new EditorId(editorId),
            Contribution = contribution
        };
    }

    [Test]
    public async Task Execute_NoWorkspaceLoaded_ReturnsEmptyList()
    {
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        var command = new GetUtilitiesStateCommand(
            workspaceWrapper,
            Substitute.For<IStringLocalizer>(),
            Substitute.For<IPackageLocalizationService>());

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Utilities.Should().BeEmpty();
    }
}
