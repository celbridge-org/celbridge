using Celbridge.Messaging;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class FocusServiceTests
{
    private IMessengerService _messengerService = null!;
    private ILogger<FocusService> _logger = null!;
    private FocusService _focusService = null!;

    [SetUp]
    public void SetUp()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _logger = Substitute.For<ILogger<FocusService>>();
        _focusService = new FocusService(_messengerService, _logger);
    }

    [Test]
    public void OnFocusReceived_TracksPanelAndTarget()
    {
        var target = Substitute.For<IEditTarget>();

        _focusService.OnFocusReceived(WorkspacePanel.Explorer, target);

        _focusService.FocusedPanel.Should().Be(WorkspacePanel.Explorer);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_DifferentPanel_ReleasesPreviousSurface()
    {
        var released = false;
        _focusService.OnFocusReceived(WorkspacePanel.Documents, onReleaseFocus: () => released = true);

        _focusService.OnFocusReceived(WorkspacePanel.Explorer);

        released.Should().BeTrue();
    }

    [Test]
    public void OnFocusReceived_SamePanelFromManagedChrome_ReleasesSurfaceAndKeepsEditTarget()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Documents, target, () => releaseCount++);

        // Only a hosted surface supplies a release callback, so a claim without one is managed chrome in the
        // same panel (the URL bar, the find bar) taking the keyboard off that surface. The edit context still
        // follows the surface, so Edit commands keep routing to it.
        _focusService.OnFocusReceived(WorkspacePanel.Documents);

        releaseCount.Should().Be(1);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_SamePanelFromManagedChromeRepeatedly_ReleasesTheSurfaceOnce()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Documents, target, () => releaseCount++);

        _focusService.OnFocusReceived(WorkspacePanel.Documents);
        _focusService.OnFocusReceived(WorkspacePanel.Documents);

        releaseCount.Should().Be(1);
    }

    [Test]
    public void OnFocusReceived_NewPanelWithTarget_ReplacesEditTarget()
    {
        var firstTarget = Substitute.For<IEditTarget>();
        var secondTarget = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Console, firstTarget);

        _focusService.OnFocusReceived(WorkspacePanel.Explorer, secondTarget);

        _focusService.EditTarget.Should().Be(secondTarget);
    }

    [Test]
    public void OnFocusReceived_NewPanelWithoutTarget_PreservesEditTarget()
    {
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Documents, target);

        // A panel that claims focus without an edit target (e.g. Search) leaves the last editing surface in
        // place, so Edit commands still route there.
        _focusService.OnFocusReceived(WorkspacePanel.Search);

        _focusService.FocusedPanel.Should().Be(WorkspacePanel.Search);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void ClearFocus_ReleasesSurfaceAndClearsPanel_ButPreservesEditTarget()
    {
        var released = false;
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Console, target, () => released = true);

        // A chrome interaction (e.g. a toolbar click) clears panel focus and releases the caret, but the edit
        // context must survive so the Edit menu still routes to the console after the toolbar takes focus.
        _focusService.ClearFocus();

        released.Should().BeTrue();
        _focusService.FocusedPanel.Should().Be(WorkspacePanel.None);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void ClearFocus_AfterReleasing_DoesNotReleaseAgain()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Console, target, () => releaseCount++);

        _focusService.ClearFocus();
        _focusService.ClearFocus();

        releaseCount.Should().Be(1);
    }

    [Test]
    public void ClearEditTarget_MatchingTarget_ClearsIt()
    {
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Documents, target);

        _focusService.ClearEditTarget(target);

        _focusService.EditTarget.Should().BeNull();
    }

    [Test]
    public void ClearEditTarget_DifferentTarget_LeavesItInPlace()
    {
        var currentTarget = Substitute.For<IEditTarget>();
        var tornDownTarget = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Documents, currentTarget);

        // A surface that already lost the edit context tearing down must not wipe the newer target.
        _focusService.ClearEditTarget(tornDownTarget);

        _focusService.EditTarget.Should().Be(currentTarget);
    }
}
