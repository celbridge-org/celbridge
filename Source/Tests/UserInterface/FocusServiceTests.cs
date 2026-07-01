using Celbridge.Messaging;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class FocusServiceTests
{
    private IMessengerService _messengerService = null!;
    private FocusService _focusService = null!;

    [SetUp]
    public void SetUp()
    {
        _messengerService = Substitute.For<IMessengerService>();
        _focusService = new FocusService(_messengerService);
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
    public void OnFocusReceived_DifferentPanel_BlursPreviousSurface()
    {
        var blurred = false;
        _focusService.OnFocusReceived(WorkspacePanel.Documents, onReleaseFocus: () => blurred = true);

        _focusService.OnFocusReceived(WorkspacePanel.Explorer);

        blurred.Should().BeTrue();
    }

    [Test]
    public void OnFocusReceived_SamePanelWithoutCallbacks_DoesNotBlurOrClear()
    {
        var blurCount = 0;
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Documents, target, () => blurCount++);

        // A bubbled report for the same panel carries no target or blur and must not wipe either.
        _focusService.OnFocusReceived(WorkspacePanel.Documents);

        blurCount.Should().Be(0);
        _focusService.EditTarget.Should().Be(target);
    }

    [Test]
    public void ClearFocus_BlursAndResets()
    {
        var blurred = false;
        var target = Substitute.For<IEditTarget>();
        _focusService.OnFocusReceived(WorkspacePanel.Documents, target, () => blurred = true);

        _focusService.ClearFocus();

        blurred.Should().BeTrue();
        _focusService.FocusedPanel.Should().Be(WorkspacePanel.None);
        _focusService.EditTarget.Should().BeNull();
    }
}
