using Celbridge.Messaging;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class FocusServiceTests
{
    private sealed class TestFocusSurface : IFocusSurface;

    private IMessengerService _messengerService = null!;
    private ILogger<FocusService> _logger = null!;
    private FocusService _focusService = null!;

    // The web surface most tests report from. Claims are compared by surface identity, so a test that needs
    // two distinct surfaces creates its own.
    private IFocusSurface _surface = null!;

    [SetUp]
    public void SetUp()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _logger = Substitute.For<ILogger<FocusService>>();
        _focusService = new FocusService(_messengerService, _logger);
        _surface = new TestFocusSurface();
    }

    [Test]
    public void OnFocusReceived_TracksPanelAndTarget()
    {
        var target = Substitute.For<IEditTarget>();

        var claim = FocusClaim.FromManagedControl(WorkspacePanelId.Explorer, target);
        _focusService.OnFocusReceived(claim);

        _focusService.FocusedPanel.Should().Be(WorkspacePanelId.Explorer);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_DifferentPanel_ReleasesPreviousSurface()
    {
        var released = false;
        var surfaceClaim = FocusClaim.FromWebSurface(WorkspacePanelId.Documents, null, _surface, () => released = true);
        _focusService.OnFocusReceived(surfaceClaim);

        var explorerClaim = FocusClaim.FromManagedControl(WorkspacePanelId.Explorer);
        _focusService.OnFocusReceived(explorerClaim);

        released.Should().BeTrue();
    }

    [Test]
    public void OnFocusReceived_SamePanelFromManagedChrome_ReleasesSurfaceAndKeepsEditTarget()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(WorkspacePanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(surfaceClaim);

        // A managed control claiming the panel a surface holds is chrome (the URL bar, the find bar) taking
        // the keyboard off that surface. The edit context still follows the surface, so Edit commands keep
        // routing to it.
        var chromeClaim = FocusClaim.FromManagedControl(WorkspacePanelId.Documents);
        _focusService.OnFocusReceived(chromeClaim);

        releaseCount.Should().Be(1);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_SamePanelFromManagedChromeRepeatedly_ReleasesTheSurfaceOnce()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(WorkspacePanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(surfaceClaim);

        var chromeClaim = FocusClaim.FromManagedControl(WorkspacePanelId.Documents);
        _focusService.OnFocusReceived(chromeClaim);
        _focusService.OnFocusReceived(chromeClaim);

        releaseCount.Should().Be(1);
    }

    [Test]
    public void OnFocusReceived_SameSurfaceReclaims_DoesNotReleaseIt()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var firstClaim = FocusClaim.FromWebSurface(WorkspacePanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(firstClaim);

        // A surface reporting its own focus again is not a move off it, so its caret is left alone. The
        // packaged Windows head reports twice for one click, because a web surface takes managed focus
        // there as well as native focus.
        var secondClaim = FocusClaim.FromWebSurface(WorkspacePanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(secondClaim);

        releaseCount.Should().Be(0);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void OnFocusReceived_SecondSurfaceInSamePanel_ReleasesTheFirst()
    {
        var firstReleaseCount = 0;
        var firstClaim = FocusClaim.FromWebSurface(
            WorkspacePanelId.Documents,
            null,
            _surface,
            () => firstReleaseCount++);
        _focusService.OnFocusReceived(firstClaim);

        // Two .webview documents both claim the Documents panel and carry no edit target, so neither the
        // panel nor the target changes when focus moves between them. Only the surface identity shows that
        // the first has lost the keyboard and must drop its caret.
        var secondSurface = new TestFocusSurface();
        var secondClaim = FocusClaim.FromWebSurface(
            WorkspacePanelId.Documents,
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
        var firstClaim = FocusClaim.FromManagedControl(WorkspacePanelId.Documents, firstTarget);
        _focusService.OnFocusReceived(firstClaim);

        var secondClaim = FocusClaim.FromManagedControl(WorkspacePanelId.Explorer, secondTarget);
        _focusService.OnFocusReceived(secondClaim);

        _focusService.EditTarget.Should().Be(secondTarget);
    }

    [Test]
    public void OnFocusReceived_NewPanelWithoutTarget_PreservesEditTarget()
    {
        var target = Substitute.For<IEditTarget>();
        var documentsClaim = FocusClaim.FromManagedControl(WorkspacePanelId.Documents, target);
        _focusService.OnFocusReceived(documentsClaim);

        // A panel that claims focus without an edit target (e.g. Search) leaves the last editing surface in
        // place, so Edit commands still route there.
        var searchClaim = FocusClaim.FromManagedControl(WorkspacePanelId.Search);
        _focusService.OnFocusReceived(searchClaim);

        _focusService.FocusedPanel.Should().Be(WorkspacePanelId.Search);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void ClearFocus_ReleasesSurfaceAndClearsPanel_ButPreservesEditTarget()
    {
        var released = false;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(WorkspacePanelId.Documents, target, _surface, () => released = true);
        _focusService.OnFocusReceived(surfaceClaim);

        // A chrome interaction (e.g. a toolbar click) clears panel focus and releases the caret, but the edit
        // context must survive so the Edit menu still routes to the console after the toolbar takes focus.
        _focusService.ClearFocus();

        released.Should().BeTrue();
        _focusService.FocusedPanel.Should().Be(WorkspacePanelId.None);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void ClearFocus_AfterReleasing_DoesNotReleaseAgain()
    {
        var releaseCount = 0;
        var target = Substitute.For<IEditTarget>();
        var surfaceClaim = FocusClaim.FromWebSurface(WorkspacePanelId.Documents, target, _surface, () => releaseCount++);
        _focusService.OnFocusReceived(surfaceClaim);

        _focusService.ClearFocus();
        _focusService.ClearFocus();

        releaseCount.Should().Be(1);
    }

    [Test]
    public void ClearEditTarget_MatchingTarget_ClearsIt()
    {
        var target = Substitute.For<IEditTarget>();
        var claim = FocusClaim.FromManagedControl(WorkspacePanelId.Documents, target);
        _focusService.OnFocusReceived(claim);

        _focusService.ClearEditTarget(target);

        _focusService.EditTarget.Should().BeNull();
    }

    [Test]
    public void ClearEditTarget_DifferentTarget_LeavesItInPlace()
    {
        var currentTarget = Substitute.For<IEditTarget>();
        var tornDownTarget = Substitute.For<IEditTarget>();
        var claim = FocusClaim.FromManagedControl(WorkspacePanelId.Documents, currentTarget);
        _focusService.OnFocusReceived(claim);

        // A surface that already lost the edit context tearing down must not wipe the newer target.
        _focusService.ClearEditTarget(tornDownTarget);

        _focusService.EditTarget.Should().Be(currentTarget);
    }
}
