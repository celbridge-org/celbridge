using Celbridge.Documents.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Direct unit test for ShowUtilityCommand. Exercises the command's own routing logic: the built-in surfaces
/// bypass the utility service, and every other id is validated against the live utilities rather than the
/// declared contributions.
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
    public async Task Execute_LiveUtilityWithLocation_DocksBeforeRevealing()
    {
        _utilityService.HasUtility(NotepadUtilityId).Returns(true);
        _utilityService.DockUtilityAsync(NotepadUtilityId, DockLocation.Document).Returns(Result.Ok());

        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = NotepadUtilityId,
            Location = DockLocation.Document
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await _utilityService.Received(1).DockUtilityAsync(NotepadUtilityId, DockLocation.Document);
        _utilityPanel.Received(1).ShowUtility(NotepadUtilityId);
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
    public async Task Execute_BuiltInUtility_RevealsWithoutConsultingTheUtilityService()
    {
        // The built-in surfaces are not contributions and are never created by the utility service, so they
        // must bypass the live-utility guard.
        var command = new ShowUtilityCommand(_workspaceWrapper)
        {
            UtilityId = BuiltInUtilityIds.Explorer
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _utilityPanel.Received(1).ShowUtility(BuiltInUtilityIds.Explorer);
        _utilityService.DidNotReceive().HasUtility(Arg.Any<EditorId>());
    }

    [Test]
    public async Task Execute_EmptyUtilityId_Fails()
    {
        var command = new ShowUtilityCommand(_workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
