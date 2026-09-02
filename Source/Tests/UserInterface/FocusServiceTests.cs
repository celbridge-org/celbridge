using Celbridge.Messaging;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class FocusServiceTests
{
    private sealed class TestFocusSurface : IFocusSurface
    {
        public string SurfaceName => "test surface";
    }

    private IMessengerService _messengerService = null!;
    private ILogger<FocusService> _logger = null!;
    private FocusService _focusService = null!;

    // The handler the service registered for workspace unload, captured so a test can raise it without a
    // real messenger.
    private MessageHandler<object, WorkspaceUnloadedMessage> _workspaceUnloadedHandler = null!;

    // The web surface most tests report from. Claims are compared by surface identity, so a test that needs
    // two distinct surfaces creates its own.
    private IFocusSurface _surface = null!;

    [SetUp]
    public void SetUp()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _messengerService
            .When(messenger => messenger.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, WorkspaceUnloadedMessage>>()))
            .Do(call => _workspaceUnloadedHandler = call.Arg<MessageHandler<object, WorkspaceUnloadedMessage>>());

        _logger = Substitute.For<ILogger<FocusService>>();
        _focusService = new FocusService(_messengerService, _logger);
        _surface = new TestFocusSurface();
    }

    [Test]
    public void OnFocusReceived_TracksPanelAndTarget()
    {
        var target = Substitute.For<IEditTarget>();

        var claim = FocusClaim.FromManagedControl(FocusPanelId.Explorer, target);
        _focusService.OnFocusReceived(claim);

        _focusService.FocusedPanel.Should().Be(FocusPanelId.Explorer);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_DifferentPanel_ReleasesPreviousSurface()
    {
        var released = false;
        var surfaceClaim = FocusClaim.FromWebSurface(FocusPanelId.Documents, null, _surface, () => released = true);
        _focusService.OnFocusReceived(surfaceClaim);

        var explorerClaim = FocusClaim.FromManagedControl(FocusPanelId.Explorer);
        _focusService.OnFocusReceived(explorerClaim);

        released.Should().BeTrue();
    }

    [Test]
    public void OnFocusReceived_SamePanelFromManagedChrome_ReleasesSurfaceAndKeepsEditTarget()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(FocusPanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(surfaceClaim);

        // A managed control claiming the panel a surface holds is chrome (the URL bar, the find bar) taking
        // the keyboard off that surface. The edit context still follows the surface, so Edit commands keep
        // routing to it.
        var chromeClaim = FocusClaim.FromManagedControl(FocusPanelId.Documents);
        _focusService.OnFocusReceived(chromeClaim);

        releaseCount.Should().Be(1);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_SamePanelFromManagedChromeRepeatedly_ReleasesTheSurfaceOnce()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(FocusPanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(surfaceClaim);

        var chromeClaim = FocusClaim.FromManagedControl(FocusPanelId.Documents);
        _focusService.OnFocusReceived(chromeClaim);
        _focusService.OnFocusReceived(chromeClaim);

        releaseCount.Should().Be(1);
    }

    [Test]
    public void OnFocusReceived_SameSurfaceReclaims_DoesNotReleaseIt()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var firstClaim = FocusClaim.FromWebSurface(FocusPanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(firstClaim);

        // A surface reporting its own focus again is not a move off it, so its caret is left alone. The
        // packaged Windows head reports twice for one click, because a web surface takes managed focus
        // there as well as native focus.
        var secondClaim = FocusClaim.FromWebSurface(FocusPanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(secondClaim);

        releaseCount.Should().Be(0);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_SecondSurfaceInSamePanel_ReleasesTheFirst()
    {
        var firstReleaseCount = 0;
        var firstClaim = FocusClaim.FromWebSurface(
            FocusPanelId.Documents,
            null,
            _surface,
            () => firstReleaseCount++);
        _focusService.OnFocusReceived(firstClaim);

        // Both surfaces claim the Documents panel, so only the surface identity marks the first as losing
        // the keyboard.
        var secondSurface = new TestFocusSurface();
        var secondClaim = FocusClaim.FromWebSurface(
            FocusPanelId.Documents,
            null,
            secondSurface,
            () => { });
        _focusService.OnFocusReceived(secondClaim);

        firstReleaseCount.Should().Be(1);
    }

    [Test]
    public void OnFocusReceived_NewPanelWithTarget_ReplacesEditTarget()
    {
        var firstTarget = Substitute.For<IEditTarget>();
        var secondTarget = Substitute.For<IEditTarget>();
        var firstClaim = FocusClaim.FromManagedControl(FocusPanelId.Documents, firstTarget);
        _focusService.OnFocusReceived(firstClaim);

        var secondClaim = FocusClaim.FromManagedControl(FocusPanelId.Explorer, secondTarget);
        _focusService.OnFocusReceived(secondClaim);

        _focusService.EditTarget.Should().Be(secondTarget);
    }

    [Test]
    public void OnFocusReceived_NewPanelWithoutTarget_ClearsEditTarget()
    {
        var target = Substitute.For<IEditTarget>();
        var explorerClaim = FocusClaim.FromManagedControl(FocusPanelId.Explorer, target);
        _focusService.OnFocusReceived(explorerClaim);

        var documentsClaim = FocusClaim.FromManagedControl(FocusPanelId.Documents);
        _focusService.OnFocusReceived(documentsClaim);

        _focusService.FocusedPanel.Should().Be(FocusPanelId.Documents);
        _focusService.EditTarget.Should().BeNull();
    }

    [Test]
    public void OnFocusReceived_ChromeInAPanelHoldingNoSurface_ClearsEditTarget()
    {
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(FocusClaim.FromManagedControl(FocusPanelId.Documents, target));

        // No surface holds a caret in this panel, so the chrome exemption does not apply.
        _focusService.OnFocusReceived(FocusClaim.FromManagedControl(FocusPanelId.Documents));

        _focusService.EditTarget.Should().BeNull();
    }

    [Test]
    public void ClearFocus_ReleasesSurfaceAndClearsPanel_ButPreservesEditTarget()
    {
        var released = false;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(FocusPanelId.Documents, target, _surface, () => released = true);
        _focusService.OnFocusReceived(surfaceClaim);

        // A chrome interaction (e.g. a toolbar click) clears panel focus and releases the caret, but the edit
        // context must survive so the Edit menu still routes to the console after the toolbar takes focus.
        _focusService.ClearFocus();

        released.Should().BeTrue();
        _focusService.FocusedPanel.Should().Be(FocusPanelId.None);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void ClearFocus_AfterReleasing_DoesNotReleaseAgain()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(FocusPanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(surfaceClaim);

        _focusService.ClearFocus();
        _focusService.ClearFocus();

        releaseCount.Should().Be(1);
    }

    [Test]
    public void ClearEditTarget_MatchingTarget_ClearsIt()
    {
        var target = Substitute.For<IEditTarget>();
        var claim = FocusClaim.FromManagedControl(FocusPanelId.Documents, target);
        _focusService.OnFocusReceived(claim);

        _focusService.ClearEditTarget(target);

        _focusService.EditTarget.Should().BeNull();
    }

    [Test]
    public void ClearEditTarget_DifferentTarget_LeavesItInPlace()
    {
        var currentTarget = Substitute.For<IEditTarget>();
        var tornDownTarget = Substitute.For<IEditTarget>();
        var claim = FocusClaim.FromManagedControl(FocusPanelId.Documents, currentTarget);
        _focusService.OnFocusReceived(claim);

        // A surface that already lost the edit context tearing down must not wipe the newer target.
        _focusService.ClearEditTarget(tornDownTarget);

        _focusService.EditTarget.Should().Be(currentTarget);
    }

    [Test]
    public void HoldPanelUntilNextInput_NamesTheHeldPanelUntilTheHoldEnds()
    {
        _focusService.HeldPanel.Should().Be(FocusPanelId.None);

        _focusService.HoldPanelUntilNextInput(FocusPanelId.Documents);
        _focusService.HeldPanel.Should().Be(FocusPanelId.Documents);

        _focusService.EndPanelHold();
        _focusService.HeldPanel.Should().Be(FocusPanelId.None);
    }

    [Test]
    public void RefocusPanel_InvokesThatPanelsFocusHandler()
    {
        var explorerHandler = Substitute.For<Action>();
        var documentsHandler = Substitute.For<Action>();
        _focusService.SetPanelFocusHandler(FocusPanelId.Explorer, explorerHandler);
        _focusService.SetPanelFocusHandler(FocusPanelId.Documents, documentsHandler);

        // The panel is named by the caller rather than taken from the focused one, which is the point of the
        // method: a document whose focus report has not arrived yet is not the focused panel.
        _focusService.OnFocusReceived(FocusClaim.FromManagedControl(FocusPanelId.Explorer));

        _focusService.RefocusPanel(FocusPanelId.Documents);

        documentsHandler.Received(1).Invoke();
        explorerHandler.DidNotReceive().Invoke();
    }

    [Test]
    public void RefocusPanel_PanelWithNoRegisteredHandler_DoesNothing()
    {
        var refocus = () => _focusService.RefocusPanel(FocusPanelId.Documents);

        refocus.Should().NotThrow();
    }

    [Test]
    public void WorkspaceUnloaded_EndsThePanelHold()
    {
        // A hold waits on the next user input, which a teardown can pre-empt, so it must not carry into the
        // next workspace.
        _focusService.HoldPanelUntilNextInput(FocusPanelId.Documents);

        _workspaceUnloadedHandler.Invoke(this, new WorkspaceUnloadedMessage());

        _focusService.HeldPanel.Should().Be(FocusPanelId.None);
    }

    [Test]
    public void WorkspaceUnloaded_ClearsPanelFocusAndTheEditTargetWithoutReleasingTheSurface()
    {
        var target = Substitute.For<IEditTarget>();
        var releaseFocus = Substitute.For<Action>();
        var claim = FocusClaim.FromWebSurface(FocusPanelId.Documents, target, _surface, releaseFocus);
        _focusService.OnFocusReceived(claim);

        // This service outlives the workspace, so a target left behind would keep the Edit menu pointing at
        // an editor that no longer exists.
        _workspaceUnloadedHandler.Invoke(this, new WorkspaceUnloadedMessage());

        _focusService.FocusedPanel.Should().Be(FocusPanelId.None);
        _focusService.EditTarget.Should().BeNull();

        // The surface went with the workspace, so the release callback would reach into a torn-down web view.
        releaseFocus.DidNotReceive().Invoke();
    }
}
