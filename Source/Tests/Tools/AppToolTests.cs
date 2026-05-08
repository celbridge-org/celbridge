using System.Text.Json;
using Celbridge.ApplicationEnvironment;
using Celbridge.Projects;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Tools;
using Celbridge.Workspace;
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
    public void GetState_ProjectLoaded()
    {
        WireAppStateDependencies();
        var projectService = Substitute.For<IProjectService>();
        var project = Substitute.For<IProject>();
        project.ProjectName.Returns("MyProject");
        projectService.CurrentProject.Returns(project);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.GetProperty("isLoaded").GetBoolean().Should().BeTrue();
        root.GetProperty("projectName").GetString().Should().Be("MyProject");
    }

    [Test]
    public void GetState_NoProjectLoaded()
    {
        WireAppStateDependencies();
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.GetProperty("isLoaded").GetBoolean().Should().BeFalse();
        root.GetProperty("projectName").GetString().Should().BeEmpty();
    }

    [Test]
    public void GetState_IncludesAgentDocsPointer()
    {
        WireAppStateDependencies();
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        var agentDocs = root.GetProperty("agentDocs");
        agentDocs.GetProperty("entry").GetString().Should().Be("agent_instructions");
        agentDocs.GetProperty("via").GetString().Should().Be("guides_read");
    }

    [Test]
    public void GetState_IncludesFocusedPanelAndLayoutMode()
    {
        WireAppStateDependencies(
            focusedPanel: WorkspacePanel.Documents,
            contextVisible: true,
            inspectorVisible: false,
            consoleVisible: true,
            consoleMaximized: false);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.GetProperty("focusedPanel").GetString().Should().Be("Documents");

        var layoutMode = root.GetProperty("layoutMode");
        layoutMode.GetProperty("contextPanelVisible").GetBoolean().Should().BeTrue();
        layoutMode.GetProperty("inspectorPanelVisible").GetBoolean().Should().BeFalse();
        layoutMode.GetProperty("consolePanelVisible").GetBoolean().Should().BeTrue();
        layoutMode.GetProperty("consoleMaximized").GetBoolean().Should().BeFalse();
    }

    [Test]
    public void GetState_IncludesFeatureFlagsForEveryKnownFlag()
    {
        var featureFlags = WireAppStateDependencies();
        // Mark just the eval flag enabled so the test verifies both true and false
        // values land in the returned payload.
        featureFlags.IsEnabled(FeatureFlagConstants.WebViewDevToolsEval).Returns(true);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        var flagsElement = root.GetProperty("featureFlags");
        flagsElement.ValueKind.Should().Be(JsonValueKind.Object);

        // Every public string constant on FeatureFlagConstants must be present.
        flagsElement.TryGetProperty(FeatureFlagConstants.ConsolePanel, out var consolePanel).Should().BeTrue();
        consolePanel.GetBoolean().Should().BeFalse();
        flagsElement.TryGetProperty(FeatureFlagConstants.McpTools, out var mcpTools).Should().BeTrue();
        mcpTools.GetBoolean().Should().BeFalse();
        flagsElement.TryGetProperty(FeatureFlagConstants.WebViewDevTools, out var webViewDevTools).Should().BeTrue();
        webViewDevTools.GetBoolean().Should().BeFalse();
        flagsElement.TryGetProperty(FeatureFlagConstants.WebViewDevToolsEval, out var webViewDevToolsEval).Should().BeTrue();
        webViewDevToolsEval.GetBoolean().Should().BeTrue();
    }

    private IFeatureFlags WireAppStateDependencies(
        WorkspacePanel focusedPanel = WorkspacePanel.None,
        bool contextVisible = false,
        bool inspectorVisible = false,
        bool consoleVisible = false,
        bool consoleMaximized = false)
    {
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(Arg.Any<string>()).Returns(false);
        _services.GetRequiredService<IFeatureFlags>().Returns(featureFlags);

        var panelFocusService = Substitute.For<IPanelFocusService>();
        panelFocusService.FocusedPanel.Returns(focusedPanel);
        _services.GetRequiredService<IPanelFocusService>().Returns(panelFocusService);

        var layoutService = Substitute.For<ILayoutService>();
        layoutService.IsContextPanelVisible.Returns(contextVisible);
        layoutService.IsInspectorPanelVisible.Returns(inspectorVisible);
        layoutService.IsConsolePanelVisible.Returns(consoleVisible);
        layoutService.IsConsoleMaximized.Returns(consoleMaximized);
        _services.GetRequiredService<ILayoutService>().Returns(layoutService);

        return featureFlags;
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
