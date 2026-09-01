using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class FocusIntentTests
{
    [SetUp]
    [TearDown]
    public void ResetSharedState()
    {
        // The state is process-wide, so each test starts and leaves it clean rather than depending on order.
        FocusIntent.Reset();
    }

    [Test]
    public void PanelHold_NamesTheHeldPanelUntilEnded()
    {
        FocusIntent.HeldPanel.Should().Be(FocusPanelId.None);

        FocusIntent.HoldPanelUntilNextInput(FocusPanelId.Documents);
        FocusIntent.HeldPanel.Should().Be(FocusPanelId.Documents);

        FocusIntent.EndPanelHold();
        FocusIntent.HeldPanel.Should().Be(FocusPanelId.None);
    }

    [Test]
    public void Reset_ClearsStateThatWouldOtherwiseOutliveTheWorkspace()
    {
        // The hold waits on the next user input, which a workspace teardown can pre-empt, so it must not
        // carry into the next workspace.
        FocusIntent.HoldPanelUntilNextInput(FocusPanelId.Documents);

        FocusIntent.Reset();

        FocusIntent.HeldPanel.Should().Be(FocusPanelId.None);
    }

    [Test]
    public void Reset_LeavesRestorationDepthAlone()
    {
        // RestoreFocus balances the depth in a finally, so zeroing it mid-call would leave the counter
        // negative and the guard permanently off. Reset must not touch it.
        FocusIntent.Reset();

        FocusIntent.IsRestorationInProgress.Should().BeFalse();
    }
}
