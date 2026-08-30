using Celbridge.Documents.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Direct unit test for ShowUtilityCommand. Exercises the command's own routing logic: an id is accepted when
/// it is a live utility or a button on the rail, and only a live utility can be moved to an area first.
/// </summary>
[TestFixture]
public class ShowUtilityCommandTests
{
    private static readonly EditorId NotepadUtilityId = EditorId.Create("acme", "notepad");

    private IUtilityPanel _utilityPanel = null!;
    private IUtilityService _utilityService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _utilityPanel = Substitute.For<IUtilityPanel>();
        _utilityService = Substitute.For<IUtilityService>();

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(_utilityPanel);
        workspaceService.UtilityService.Returns(_utilityService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    [Test]
    public async Task Execute_LiveUtility_RevealsIt()
    {
        _utilityService.HasUtility(NotepadUtilityId).Returns(true);

        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = NotepadUtilityId
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _utilityPanel.Received(1).ShowUtility(NotepadUtilityId);
    }

    [Test]
    public async Task Execute_LiveUtilityWithArea_DocksBeforeRevealing()
    {
        _utilityService.HasUtility(NotepadUtilityId).Returns(true);
        _utilityService.DockUtilityAsync(NotepadUtilityId, WorkspaceArea.Main).Returns(Result.Ok());

        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = NotepadUtilityId,
            Area = WorkspaceArea.Main
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _utilityService.Received(1).DockUtilityAsync(NotepadUtilityId, WorkspaceArea.Main);
        _utilityPanel.Received(1).ShowUtility(NotepadUtilityId);
    }

    [Test]
    public async Task Execute_DisallowedArea_ReportsWhatTheServiceSays()
    {
        // The declaration is enforced by the service, so the command carries its error rather than
        // second-guessing it.
        _utilityService.HasUtility(NotepadUtilityId).Returns(true);
        _utilityService.DockUtilityAsync(NotepadUtilityId, WorkspaceArea.Side)
            .Returns(Result.Fail("it allows 'utility', 'main'."));

        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = NotepadUtilityId,
            Area = WorkspaceArea.Side
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _utilityPanel.DidNotReceive().ShowUtility(Arg.Any<EditorId>());
    }

    [Test]
    public async Task Execute_DeclaredButNotLiveUtility_FailsRatherThanSilentlyDoingNothing()
    {
        // A utility that was declared but skipped at load (disabled feature flag, failed seed or init) is
        // not live, and ShowUtility would reveal nothing for it.
        _utilityService.HasUtility(NotepadUtilityId).Returns(false);

        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = NotepadUtilityId
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        _utilityPanel.DidNotReceive().ShowUtility(Arg.Any<EditorId>());
    }

    [Test]
    public async Task Execute_BuiltInSurface_RevealsEvenThoughItIsNotALiveUtility()
    {
        // The built-in surfaces are not contributions and are never created by the utility service, so the
        // rail is what says they exist.
        _utilityPanel.HasRailItem(BuiltInUtilityIds.Explorer).Returns(true);

        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = BuiltInUtilityIds.Explorer
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _utilityPanel.Received(1).ShowUtility(BuiltInUtilityIds.Explorer);
    }

    [Test]
    public async Task Execute_Launcher_RevealsItsDocument()
    {
        // A launcher opens a document rather than occupying the panel, so it is not a live utility either.
        // An agent asked for a button the user can see has to be able to reveal it.
        _utilityPanel.HasRailItem(BuiltInLauncherIds.Workshop).Returns(true);

        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = BuiltInLauncherIds.Workshop
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _utilityPanel.Received(1).ShowUtility(BuiltInLauncherIds.Workshop);

        // Only a live utility can be moved between areas, so a launcher never reaches the dock path.
        await _utilityService.DidNotReceive().DockUtilityAsync(Arg.Any<EditorId>(), Arg.Any<WorkspaceArea>());
    }

    [Test]
    public async Task Execute_EmptyUtilityId_Fails()
    {
        var command = new ShowUtilityCommand(_workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
