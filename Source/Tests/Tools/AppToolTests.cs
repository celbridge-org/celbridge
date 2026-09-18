using System.Text.Json;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Resources;
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
    public void GetState_IncludesAppVersion()
    {
        WireAppStateDependencies(appVersion: "1.2.3");
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.GetProperty("version").GetString().Should().Be("1.2.3");
    }

    [Test]
    public void GetState_IncludesBuildConfiguration()
    {
        WireAppStateDependencies(configuration: "Release");
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.GetProperty("configuration").GetString().Should().Be("Release");
    }

    [Test]
    public void GetState_DoesNotIncludeAgentDocs()
    {
        // The agentDocs pointer is intentionally absent because the orientation
        // guide auto-attaches on first tool use.
        WireAppStateDependencies();
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.TryGetProperty("agentDocs", out _).Should().BeFalse();
    }

    [Test]
    public void GetState_IncludesFocusedPanelAndLayoutMode()
    {
        WireAppStateDependencies(
            focusedPanel: FocusPanelId.Documents,
            contextVisible: true,
            inspectorVisible: false,
            consoleVisible: true);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.GetProperty("focusedPanel").GetString().Should().Be("Documents");

        // Every area is reported by its token, including Main, which the Default layout always shows.
        var areaVisibility = root.GetProperty("layoutMode").GetProperty("areaVisibility");
        areaVisibility.GetProperty("utility").GetBoolean().Should().BeTrue();
        areaVisibility.GetProperty("main").GetBoolean().Should().BeTrue();
        areaVisibility.GetProperty("side").GetBoolean().Should().BeFalse();
        areaVisibility.GetProperty("bottom").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void GetState_InFocusShowingTheBottomArea_ReportsOnlyThatArea()
    {
        WireAppStateDependencies();
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        // Focus shows the Bottom area on its own, with Main off screen.
        var layoutService = _services.GetRequiredService<ILayoutService>();
        var presentedAreas = new HashSet<WorkspaceArea>
        {
            WorkspaceArea.Bottom
        };
        layoutService.PresentedAreas.Returns(presentedAreas);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        var areaVisibility = root.GetProperty("layoutMode").GetProperty("areaVisibility");
        areaVisibility.GetProperty("utility").GetBoolean().Should().BeFalse();
        areaVisibility.GetProperty("main").GetBoolean().Should().BeFalse();
        areaVisibility.GetProperty("side").GetBoolean().Should().BeFalse();
        areaVisibility.GetProperty("bottom").GetBoolean().Should().BeTrue();
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
        flagsElement.TryGetProperty(FeatureFlagConstants.WebViewDevTools, out var webViewDevTools).Should().BeTrue();
        webViewDevTools.GetBoolean().Should().BeFalse();
        flagsElement.TryGetProperty(FeatureFlagConstants.WebViewDevToolsEval, out var webViewDevToolsEval).Should().BeTrue();
        webViewDevToolsEval.GetBoolean().Should().BeTrue();
        flagsElement.TryGetProperty(FeatureFlagConstants.WebViewLoadDiagnostics, out var webViewLoadDiagnostics).Should().BeTrue();
        webViewLoadDiagnostics.GetBoolean().Should().BeFalse();
        flagsElement.TryGetProperty(FeatureFlagConstants.OpenCel, out var openCel).Should().BeTrue();
        openCel.GetBoolean().Should().BeFalse();
        flagsElement.TryGetProperty(FeatureFlagConstants.NoteEditor, out var noteEditor).Should().BeTrue();
        noteEditor.GetBoolean().Should().BeFalse();
    }

    [Test]
    public void ListPackages_ReportsProjectPackagesByName_AndProjectFailures()
    {
        WireProjectPackages();

        var tools = new AppTools(_services);
        var root = ParseResult(tools.ListPackages());

        // The bundled package and the bundled load failure are not part of the project's state.
        var packages = root.GetProperty("packages");
        packages.GetArrayLength().Should().Be(2);
        packages[0].GetProperty("name").GetString().Should().Be("acme-alpha");
        packages[0].GetProperty("packageVersion").GetString().Should().Be("1.0.0");
        packages[0].GetProperty("folder").GetString().Should().Be("project:packages/acme-alpha");
        packages[1].GetProperty("name").GetString().Should().Be("acme-beta");
        packages[1].GetProperty("packageVersion").GetString().Should().Be("2.1.0");

        var failures = root.GetProperty("failures");
        failures.GetArrayLength().Should().Be(1);
        failures[0].GetProperty("folder").GetString().Should().Be("project:packages/acme-broken");
        failures[0].GetProperty("reason").GetString().Should().Be("InvalidManifest");
        failures[0].GetProperty("detail").GetString().Should().Be("'package-version': '2.1' is not a three-part version such as 1.0.0.");
    }

    [Test]
    public void ListPackages_NoProjectLoaded_ReturnsAnError()
    {
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(false);
        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);

        var tools = new AppTools(_services);
        var result = tools.ListPackages();

        result.IsError.Should().BeTrue();
    }

    [Test]
    public void GetState_ProjectLoaded_SummarizesTheListedPackages()
    {
        WireAppStateDependencies();
        WireProjectPackages();
        var projectService = Substitute.For<IProjectService>();
        var project = Substitute.For<IProject>();
        project.ProjectName.Returns("MyProject");
        projectService.CurrentProject.Returns(project);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var state = ParseResult(tools.GetState());
        var listed = ParseResult(tools.ListPackages());

        var summary = state.GetProperty("packages");
        var listedPackages = listed.GetProperty("packages");
        summary.GetArrayLength().Should().Be(2);
        summary.GetArrayLength().Should().Be(listedPackages.GetArrayLength());
        for (var index = 0; index < summary.GetArrayLength(); index++)
        {
            summary[index].GetProperty("name").GetString()
                .Should().Be(listedPackages[index].GetProperty("name").GetString());
            summary[index].GetProperty("packageVersion").GetString()
                .Should().Be(listedPackages[index].GetProperty("packageVersion").GetString());
        }

        state.GetProperty("packageLoadFailureCount").GetInt32()
            .Should().Be(listed.GetProperty("failures").GetArrayLength());
    }

    [Test]
    public void GetState_NoProjectLoaded_ReportsAnEmptyPackageSummary()
    {
        WireAppStateDependencies();
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns((IProject?)null);
        _services.GetRequiredService<IProjectService>().Returns(projectService);

        var tools = new AppTools(_services);
        var root = ParseResult(tools.GetState());

        root.GetProperty("packages").GetArrayLength().Should().Be(0);
        root.GetProperty("packageLoadFailureCount").GetInt32().Should().Be(0);
    }

    // A loaded workspace whose registry holds two project packages out of name order, a bundled package, a load
    // failure in the project tree and a bundled load failure. Paths under the project folder resolve to project:
    // keys.
    private void WireProjectPackages()
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(AppToolTests));
        var alphaFolder = Path.Combine(projectFolder, "packages", "acme-alpha");
        var betaFolder = Path.Combine(projectFolder, "packages", "acme-beta");
        var bundledFolder = Path.Combine(Path.GetTempPath(), "Celbridge", $"{nameof(AppToolTests)}Bundled");

        var packageService = Substitute.For<IPackageService>();
        packageService.GetAllPackages().Returns(
        [
            CreatePackage("celbridge-acme", PackageOrigin.Bundled, Path.Combine(bundledFolder, "celbridge-acme"), SemanticVersion.Default),
            CreatePackage("acme-beta", PackageOrigin.Project, betaFolder, new SemanticVersion(2, 1, 0)),
            CreatePackage("acme-alpha", PackageOrigin.Project, alphaFolder, SemanticVersion.Default),
        ]);
        packageService.GetLoadFailures().Returns(
        [
            new PackageLoadFailure
            {
                Folder = Path.Combine(bundledFolder, "celbridge-broken"),
                Reason = PackageLoadFailureReason.InvalidManifest,
                Origin = PackageOrigin.Bundled
            },
            new PackageLoadFailure
            {
                Folder = Path.Combine(projectFolder, "packages", "acme-broken"),
                Reason = PackageLoadFailureReason.InvalidManifest,
                Detail = "'package-version': '2.1' is not a three-part version such as 1.0.0.",
                Origin = PackageOrigin.Project
            },
        ]);

        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.GetResourceKey(Arg.Any<string>()).Returns(callInfo =>
        {
            var path = callInfo.Arg<string>();
            var relativePath = Path.GetRelativePath(projectFolder, path).Replace(Path.DirectorySeparatorChar, '/');
            return Result<ResourceKey>.Ok(new ResourceKey(relativePath));
        });

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.PackageService.Returns(packageService);
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);
        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);
    }

    private static Package CreatePackage(string name, PackageOrigin origin, string packageFolder, SemanticVersion packageVersion)
    {
        return new Package
        {
            Info = new PackageInfo
            {
                Name = name,
                Origin = origin,
                PackageFolder = packageFolder,
                PackageVersion = packageVersion
            }
        };
    }

    private IFeatureFlags WireAppStateDependencies(
        FocusPanelId focusedPanel = FocusPanelId.None,
        bool contextVisible = false,
        bool inspectorVisible = false,
        bool consoleVisible = false,
        string appVersion = "0.0.0",
        string configuration = "Debug")
    {
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(Arg.Any<string>()).Returns(false);

        var environmentService = Substitute.For<IAppEnvironment>();
        var environmentInfo = new EnvironmentInfo(appVersion, "Windows", configuration);
        environmentService.GetEnvironmentInfo().Returns(environmentInfo);

        var focusService = Substitute.For<IFocusService>();
        focusService.FocusedPanel.Returns(focusedPanel);

        var layoutService = Substitute.For<ILayoutService>();

        // The Default layout always has Main on screen, so the substitute has to say so too.
        var presentedAreas = new HashSet<WorkspaceArea>
        {
            WorkspaceArea.Main
        };
        if (contextVisible)
        {
            presentedAreas.Add(WorkspaceArea.Utility);
        }
        if (inspectorVisible)
        {
            presentedAreas.Add(WorkspaceArea.Side);
        }
        if (consoleVisible)
        {
            presentedAreas.Add(WorkspaceArea.Bottom);
        }
        layoutService.PresentedAreas.Returns(presentedAreas);

        _services.GetRequiredService<IFeatureFlags>().Returns(featureFlags);
        _services.GetRequiredService<IAppEnvironment>().Returns(environmentService);
        _services.GetRequiredService<IFocusService>().Returns(focusService);
        _services.GetRequiredService<ILayoutService>().Returns(layoutService);

        // No workspace is loaded until a test wires one, so the package summary starts empty.
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(false);
        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);

        // AppTools.GetState resolves IAppStateProvider. Build a real provider
        // that wraps the substituted underlying services so the existing
        // JSON-shape assertions continue to exercise the full build path. The
        // factory re-resolves IProjectService and IWorkspaceWrapper at call time
        // so tests that override either after WireAppStateDependencies returns
        // (most of them) see their override.
        var spotlightRegistry = Substitute.For<ISpotlightRegistry>();
        spotlightRegistry.GetLandmarks().Returns(new List<LandmarkDescriptor>());

        _services.GetRequiredService<IAppStateProvider>().Returns(
            _ => new AppStateProvider(
                environmentService,
                _services.GetRequiredService<IProjectService>(),
                _services.GetRequiredService<IWorkspaceWrapper>(),
                featureFlags,
                focusService,
                layoutService,
                spotlightRegistry,
                Substitute.For<IMessengerService>()));

        return featureFlags;
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
