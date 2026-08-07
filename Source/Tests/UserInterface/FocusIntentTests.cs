using Celbridge.UserInterface;

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
    public void PanelClaimSuppression_HoldsUntilEnded()
    {
        FocusIntent.IsPanelClaimSuppressed.Should().BeFalse();

        FocusIntent.SuppressPanelClaimsUntilNextInput();
        FocusIntent.IsPanelClaimSuppressed.Should().BeTrue();

        FocusIntent.EndPanelClaimSuppression();
        FocusIntent.IsPanelClaimSuppressed.Should().BeFalse();
    }

    [Test]
    public void Reset_ClearsStateThatWouldOtherwiseOutliveTheWorkspace()
    {
        // The hold waits on the next user input, which a workspace teardown can pre-empt, so it must not
        // carry into the next workspace.
        FocusIntent.SuppressPanelClaimsUntilNextInput();

        FocusIntent.Reset();

        FocusIntent.IsPanelClaimSuppressed.Should().BeFalse();
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
