using System.Text.Json;
using Celbridge.ApplicationEnvironment;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the AppTools MCP tool methods.
/// </summary>
[TestFixture]
public class AppToolTests
{
    private IApplicationServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
    }

    [Test]
    public void AppVersion_ReturnsVersionString()
    {
        var environmentService = Substitute.For<IEnvironmentService>();
        var environmentInfo = new EnvironmentInfo("1.2.3", "Windows", "Debug");
        environmentService.GetEnvironmentInfo().Returns(environmentInfo);
        _services.GetRequiredService<IEnvironmentService>().Returns(environmentService);

        var tools = new AppTools(_services);
        var text = GetResultText(tools.AppVersion());

        text.Should().Be("1.2.3");
    }

    [Test]
    public void GetProjectStatus_ProjectLoaded()
    {
        var projectService = Substitute.For<IProjectService>();
        var project = Substitute.For<IProject>();
        project.ProjectName.Returns("MyProject");
        projectService.CurrentProject.Returns(project);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetProjectStatus());

        root.GetProperty("isLoaded").GetBoolean().Should().BeTrue();
        root.GetProperty("projectName").GetString().Should().Be("MyProject");
    }

    [Test]
    public void GetProjectStatus_NoProjectLoaded()
    {
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetProjectStatus());

        root.GetProperty("isLoaded").GetBoolean().Should().BeFalse();
        root.GetProperty("projectName").GetString().Should().BeEmpty();
    }

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
