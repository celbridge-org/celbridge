using Celbridge.LanguageServers.Services;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the start settings the launcher builds for a definition's server: the command, the working folder and
/// the environment.
/// </summary>
[TestFixture]
public class LanguageServerLauncherTests
{
    private const string WorkingFolderPath = "/projects/demo";
    private const string InheritedVariable = "PATH";

    private ILanguageServerDefinition _definition = null!;

    [SetUp]
    public void Setup()
    {
        _definition = Substitute.For<ILanguageServerDefinition>();
    }

    [Test]
    public void CreateStartInfo_UsesTheInvocationsCommandAndTheWorkingFolder()
    {
        SetInvocation(new Dictionary<string, string>());

        var startInfo = LanguageServerLauncher.CreateStartInfo(_definition, WorkingFolderPath);

        startInfo.FileName.Should().Be("fake-server");
        startInfo.ArgumentList.Should().Equal("--stdio");
        startInfo.WorkingDirectory.Should().Be(WorkingFolderPath);
    }

    [Test]
    public void CreateStartInfo_LeavesOutAVariableTheDefinitionsFilterRemoves()
    {
        SetInvocation(new Dictionary<string, string>());
        _definition
            .When(definition => definition.FilterInheritedEnvironment(Arg.Any<IDictionary<string, string>>()))
            .Do(callInfo => callInfo.Arg<IDictionary<string, string>>().Remove(InheritedVariable));

        var startInfo = LanguageServerLauncher.CreateStartInfo(_definition, WorkingFolderPath);

        startInfo.Environment.ContainsKey(InheritedVariable).Should().BeFalse();
    }

    [Test]
    public void CreateStartInfo_SetsTheInvocationsVariablesAfterTheFilter()
    {
        SetInvocation(new Dictionary<string, string> { [InheritedVariable] = "/server/bin" });
        _definition
            .When(definition => definition.FilterInheritedEnvironment(Arg.Any<IDictionary<string, string>>()))
            .Do(callInfo => callInfo.Arg<IDictionary<string, string>>().Remove(InheritedVariable));

        var startInfo = LanguageServerLauncher.CreateStartInfo(_definition, WorkingFolderPath);

        startInfo.Environment[InheritedVariable].Should().Be("/server/bin");
    }

    private void SetInvocation(Dictionary<string, string> environmentVariables)
    {
        var invocation = new LanguageServerInvocation("fake-server", new List<string> { "--stdio" }, environmentVariables);
        _definition.CreateInvocation().Returns(invocation);
    }
}
