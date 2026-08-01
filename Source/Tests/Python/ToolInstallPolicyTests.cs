using Celbridge.Python.Services;

namespace Celbridge.Tests.Python;

[TestFixture]
public class ToolInstallPolicyTests
{
    [Test]
    public void HealthyToolWithUnchangedWheel_IsSkipped()
    {
        var decision = ToolInstallPolicy.Decide(
            ToolEnvironmentHealth.Healthy,
            wheelHashChanged: false,
            hasRunningSessions: false);

        decision.Should().Be(ToolInstallDecision.Skip);
    }

    [Test]
    public void MissingTool_IsInstalled()
    {
        var decision = ToolInstallPolicy.Decide(
            ToolEnvironmentHealth.Missing,
            wheelHashChanged: true,
            hasRunningSessions: false);

        decision.Should().Be(ToolInstallDecision.Install);
    }

    [Test]
    public void IncompleteTool_IsReinstalledEvenWhenTheWheelIsUnchanged()
    {
        // The state a reinstall interrupted by a running console leaves behind: the environment is there
        // but no longer carries the celbridge package, so the wheel hash alone would call it current.
        var decision = ToolInstallPolicy.Decide(
            ToolEnvironmentHealth.Incomplete,
            wheelHashChanged: false,
            hasRunningSessions: false);

        decision.Should().Be(ToolInstallDecision.Install);
    }

    [Test]
    public void HealthyToolWithChangedWheel_IsReinstalled()
    {
        var decision = ToolInstallPolicy.Decide(
            ToolEnvironmentHealth.Healthy,
            wheelHashChanged: true,
            hasRunningSessions: false);

        decision.Should().Be(ToolInstallDecision.Install);
    }

    [Test]
    public void RunningSessions_DeferAnInstallThatWouldOtherwiseRun()
    {
        var decision = ToolInstallPolicy.Decide(
            ToolEnvironmentHealth.Incomplete,
            wheelHashChanged: true,
            hasRunningSessions: true);

        decision.Should().Be(ToolInstallDecision.Defer);
    }

    [Test]
    public void RunningSessions_DoNotAffectAToolThatIsAlreadyCurrent()
    {
        var decision = ToolInstallPolicy.Decide(
            ToolEnvironmentHealth.Healthy,
            wheelHashChanged: false,
            hasRunningSessions: true);

        decision.Should().Be(ToolInstallDecision.Skip);
    }
}
