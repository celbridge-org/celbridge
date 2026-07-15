using Celbridge.Documents;
using Celbridge.Documents.Commands;
using Celbridge.Packages;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Direct unit test for GetUtilitiesStateCommand. Exercises the command's own catalog-building logic:
/// the built-in Explorer/Search surfaces, filtering non-utility contributions, and the isShown rule
/// (the utility that is the active rail surface).
/// </summary>
[TestFixture]
public class GetUtilitiesStateCommandTests
{
    [Test]
    public async Task Execute_ReturnsBuiltInsAndContributedUtilitiesWithShownState()
    {
        var panelUtility = new CustomDocumentEditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "emoji-panel",
            DisplayName = "Emoji Panel",
            UtilityDescriptor = new UtilityDescriptor { Resource = "utils:emoji._emoji" }
        };

        var documentUtility = new CustomDocumentEditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "notepad",
            DisplayName = "Notepad",
            UtilityDescriptor = new UtilityDescriptor { Resource = "utils:notepad._notepad" }
        };

        // A non-utility editor contribution must be filtered out of the catalog.
        var nonUtility = new CustomDocumentEditorContribution
        {
            Package = new PackageInfo { Name = "celbridge" },
            Id = "code-editor",
            DisplayName = "Code Editor"
        };

        var contributions = new List<DocumentEditorContribution>
        {
            panelUtility,
            documentUtility,
            nonUtility
        };

        // The emoji-panel utility is the active rail surface, so it is the only utility shown.
        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(UtilityId.Create("acme", "emoji-panel"));

        var packageService = Substitute.For<IPackageService>();
        packageService.GetAllDocumentEditors().Returns(contributions);

        // No utilities are docked: no documents are open.
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.GetOpenDocuments().Returns(Array.Empty<OpenDocumentInfo>());
        documentsService.ActiveDocument.Returns(ResourceKey.Empty);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(utilityPanel);
        workspaceService.PackageService.Returns(packageService);
        workspaceService.DocumentsService.Returns(documentsService);

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

        utilities[2].UtilityId.Should().Be(UtilityId.Create("acme", "emoji-panel"));
        utilities[2].DisplayName.Should().Be("Emoji Panel");
        utilities[2].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[2].IsShown.Should().BeTrue();

        utilities[3].UtilityId.Should().Be(UtilityId.Create("acme", "notepad"));
        utilities[3].DisplayName.Should().Be("Notepad");
        utilities[3].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[3].IsShown.Should().BeFalse();
    }

    [Test]
    public async Task Execute_DockedUtility_ReportsDockedAndShownByActiveDocument()
    {
        var dockedUtility = new CustomDocumentEditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "notepad",
            DisplayName = "Notepad",
            UtilityDescriptor = new UtilityDescriptor { Resource = "utils:notepad._notepad" }
        };

        var contributions = new List<DocumentEditorContribution> { dockedUtility };

        var utilityResource = new ResourceKey("utils:notepad._notepad");

        // The rail is showing Explorer; the utility is not the active rail surface. It is instead docked as a
        // document tab and is the active document, so it must be reported as docked and shown.
        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);

        var packageService = Substitute.For<IPackageService>();
        packageService.GetAllDocumentEditors().Returns(contributions);

        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.GetOpenDocuments().Returns(new List<OpenDocumentInfo>
        {
            new(utilityResource, new DocumentAddress(0, 0, 0), new DocumentEditorId("acme.notepad"))
        });
        documentsService.ActiveDocument.Returns(utilityResource);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(utilityPanel);
        workspaceService.PackageService.Returns(packageService);
        workspaceService.DocumentsService.Returns(documentsService);

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
        var notepad = command.ResultValue.Utilities.Single(utility => utility.UtilityId == UtilityId.Create("acme", "notepad"));
        notepad.Location.Should().Be(DockLocation.Document);
        notepad.IsShown.Should().BeTrue();
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
