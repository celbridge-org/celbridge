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
            Id = "emoji",
            DisplayName = "Emoji Panel",
            UtilityDescriptor = new UtilityDescriptor { ResourceExtension = "._emoji" }
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

        var instances = new List<EditorInstance>
        {
            CreateInstance("emoji-panel", panelUtility),
            CreateInstance("notepad", documentUtility),
            CreateInstance("code", nonUtility)
        };

        // The emoji-panel utility is the active rail surface, so it is the only utility shown.
        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(new EditorInstanceId("emoji-panel"));

        var packageService = Substitute.For<IPackageService>();
        packageService.GetEditorInstances().Returns(instances);

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

        utilities[2].UtilityId.Should().Be(new EditorInstanceId("emoji-panel"));
        utilities[2].DisplayName.Should().Be("Emoji Panel");
        utilities[2].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[2].IsShown.Should().BeTrue();

        utilities[3].UtilityId.Should().Be(new EditorInstanceId("notepad"));
        utilities[3].DisplayName.Should().Be("Notepad");
        utilities[3].Location.Should().Be(DockLocation.UtilityPanel);
        utilities[3].IsShown.Should().BeFalse();
    }

    [Test]
    public async Task Execute_TwoInstancesOfOneContribution_AreReportedSeparatelyWithTitleOverrides()
    {
        // Two instances of one utility contribution are distinguished by their instance-level
        // title override, each backed by its own state file.
        var consoleContribution = new EditorContribution
        {
            Package = new PackageInfo { Name = "acme" },
            Id = "console",
            DisplayName = "Console",
            UtilityDescriptor = new UtilityDescriptor { ResourceExtension = "._console" }
        };

        var instances = new List<EditorInstance>
        {
            CreateInstance("python-repl", consoleContribution, title: "Python"),
            CreateInstance("claude-cli", consoleContribution, title: "Claude")
        };

        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);

        var packageService = Substitute.For<IPackageService>();
        packageService.GetEditorInstances().Returns(instances);

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

        var pythonConsole = utilities.Single(utility => utility.UtilityId == new EditorInstanceId("python-repl"));
        pythonConsole.DisplayName.Should().Be("Python");

        var claudeConsole = utilities.Single(utility => utility.UtilityId == new EditorInstanceId("claude-cli"));
        claudeConsole.DisplayName.Should().Be("Claude");
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

        var instances = new List<EditorInstance> { CreateInstance("notepad", dockedUtility) };

        // The backing resource is derived from the instance id and the contribution's extension.
        var utilityResource = new ResourceKey("utils:notepad._notepad");

        // The rail is showing Explorer, so the utility is not the active rail surface. It is instead docked
        // as a document tab and is the active document, so it must be reported as docked and shown.
        var utilityPanel = Substitute.For<IUtilityPanel>();
        utilityPanel.ActiveUtilityId.Returns(BuiltInUtilityIds.Explorer);

        var packageService = Substitute.For<IPackageService>();
        packageService.GetEditorInstances().Returns(instances);

        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.GetOpenDocuments().Returns(new List<OpenDocumentInfo>
        {
            new(utilityResource, new DocumentAddress(0, 0, 0), new EditorInstanceId("notepad"))
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
        var notepad = command.ResultValue.Utilities.Single(utility => utility.UtilityId == new EditorInstanceId("notepad"));
        notepad.Location.Should().Be(DockLocation.Document);
        notepad.IsShown.Should().BeTrue();
    }

    private static EditorInstance CreateInstance(string instanceId, EditorContribution contribution, string? title = null)
    {
        return new EditorInstance
        {
            InstanceId = new EditorInstanceId(instanceId),
            Contribution = contribution,
            Title = title
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
