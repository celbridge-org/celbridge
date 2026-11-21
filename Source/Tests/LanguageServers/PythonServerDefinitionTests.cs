using Celbridge.LanguageServers.Services;
using Celbridge.Python;
using Celbridge.Settings;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the Python server's settings, the server's start command and the files reported to the server.
/// </summary>
[TestFixture]
public class PythonServerDefinitionTests
{
    private const string ServerPath = "/dependencies/basedpyright-langserver";
    private const string InterpreterPath = "/python/celbridge/bin/python";

    [Test]
    public void CreateConfiguration_ThePythonSection_NamesTheInterpreter()
    {
        var definition = CreateDefinition();

        var settings = definition.CreateConfiguration("python");

        settings.Should().NotBeNull();
        settings!["pythonPath"]!.GetValue<string>().Should().Be(InterpreterPath);
    }

    [Test]
    public void CreateConfiguration_TheBasedpyrightSection_SetsTheModeAndTheImportSeverity()
    {
        var definition = CreateDefinition();

        var settings = definition.CreateConfiguration("basedpyright");

        settings.Should().NotBeNull();
        var analysis = settings!["analysis"]!;
        analysis["typeCheckingMode"]!.GetValue<string>().Should().Be("standard");
        analysis["diagnosticSeverityOverrides"]!["reportMissingImports"]!.GetValue<string>().Should().Be("warning");
    }

    [Test]
    public void CreateConfiguration_TheAnalysisSection_SetsTheModeAndTheImportSeverity()
    {
        var definition = CreateDefinition();

        var settings = definition.CreateConfiguration("basedpyright.analysis");

        settings.Should().NotBeNull();
        settings!["typeCheckingMode"]!.GetValue<string>().Should().Be("standard");
        settings["diagnosticSeverityOverrides"]!["reportMissingImports"]!.GetValue<string>().Should().Be("warning");
    }

    [Test]
    public void CreateConfiguration_AnotherSection_ReturnsNull()
    {
        var definition = CreateDefinition();

        var settings = definition.CreateConfiguration("editor");

        settings.Should().BeNull();
    }

    [Test]
    public void CreateInvocation_StartsTheServerOverStdioWithoutTheInheritedPythonVariables()
    {
        var definition = CreateDefinition();

        var invocation = definition.CreateInvocation();

        invocation.ExecutablePath.Should().Be(ServerPath);
        invocation.Arguments.Should().Equal("--stdio");
        invocation.EnvironmentVariables.Should().BeEmpty();
        invocation.RemovedEnvironmentVariables.Should().Equal("PYTHONHOME", "PYTHONPATH");
    }

    [Test]
    public void Definition_BelongsToThePythonFeatureAndCoversPythonFiles()
    {
        var definition = CreateDefinition();

        definition.Feature.Should().Be(FeatureFlagCatalog.Python);
        definition.LanguageIds[".py"].Should().Be("python");
        definition.LanguageIds[".pyi"].Should().Be("python");
    }

    [Test]
    public void ReportedFiles_NamesPythonFilesAndTheConfigFiles()
    {
        var definition = CreateDefinition();

        var reportedFiles = definition.ReportedFiles;

        reportedFiles.Should().NotBeNull();
        reportedFiles!.Extensions.Should().BeEquivalentTo(".py", ".pyi");
        reportedFiles.FileNames.Should().BeEquivalentTo("pyrightconfig.json", "pyproject.toml", "py.typed");
    }

    private static PythonServerDefinition CreateDefinition()
    {
        var environment = Substitute.For<IPythonLanguageServerEnvironment>();
        environment.ServerExecutablePath.Returns(ServerPath);
        environment.InterpreterPath.Returns(InterpreterPath);
        environment.GetInheritedVariablesToRemove().Returns(new List<string> { "PYTHONHOME", "PYTHONPATH" });

        return new PythonServerDefinition(environment);
    }
}
