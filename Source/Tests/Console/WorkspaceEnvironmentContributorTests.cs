using Celbridge.Console;
using Celbridge.Console.Services;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Server;

namespace Celbridge.Tests.Console;

[TestFixture]
public class WorkspaceEnvironmentContributorTests
{
    private static readonly string ProjectFolder = OperatingSystem.IsWindows() ? @"C:\Projects\Acme" : "/projects/acme";
    private static readonly string DataFolder = OperatingSystem.IsWindows() ? @"C:\Data\Acme" : "/data/acme";

    private WorkspaceEnvironmentContributor _contributor = null!;

    [SetUp]
    public void Setup()
    {
        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.GetEnvironmentInfo().Returns(new EnvironmentInfo("1.8.0", "Test", "Release"));

        var serverService = Substitute.For<IServerService>();
        serverService.Port.Returns(4123);

        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(ProjectFolder);
        project.ProjectDataFolderPath.Returns(DataFolder);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        _contributor = new WorkspaceEnvironmentContributor(appEnvironment, serverService, projectService);
    }

    [Test]
    public async Task Contribute_AddsTheGeneralVariables()
    {
        var environment = new Dictionary<string, string>();

        await _contributor.ContributeAsync(MakeContext(), environment);

        environment["CELBRIDGE_MCP_PORT"].Should().Be("4123");
        environment["CELBRIDGE_PROJECT_FOLDER"].Should().Be(ProjectFolder);
        environment["CELBRIDGE_TEMP_FOLDER"].Should().Be(Path.Combine(DataFolder, "temp"));
        environment["CELBRIDGE_LOGS_FOLDER"].Should().Be(Path.Combine(DataFolder, "logs"));
        environment["CELBRIDGE_VERSION"].Should().Be("1.8.0");
    }

    [Test]
    public async Task Contribute_KeepsAValueTheConsoleSets()
    {
        var environment = new Dictionary<string, string>
        {
            ["CELBRIDGE_TEMP_FOLDER"] = "console-temp",
        };

        await _contributor.ContributeAsync(MakeContext(), environment);

        environment["CELBRIDGE_TEMP_FOLDER"].Should().Be("console-temp");
    }

    private static ConsoleSessionContext MakeContext()
    {
        return new ConsoleSessionContext(
            ResourceKey.Empty,
            "shell",
            string.Empty,
            new Dictionary<string, string>(),
            ProjectFolder,
            string.Empty,
            new Dictionary<string, object?>());
    }
}
