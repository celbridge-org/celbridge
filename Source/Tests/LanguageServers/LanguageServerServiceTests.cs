using System.Text.Json.Nodes;
using Celbridge.LanguageServers;
using Celbridge.LanguageServers.Services;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the service that runs the language servers.
/// </summary>
[TestFixture]
public class LanguageServerServiceTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private static readonly string ProjectFolderPath = OperatingSystem.IsWindows()
        ? @"D:\Projects\Acme"
        : "/Projects/Acme";

    private FakeLauncher _launcher = null!;
    private IFeatureFlags _featureFlags = null!;
    private FakeTimeProvider _timeProvider = null!;
    private LanguageServerService _service = null!;

    [SetUp]
    public void Setup()
    {
        _launcher = new FakeLauncher();
        _timeProvider = new FakeTimeProvider();

        _featureFlags = Substitute.For<IFeatureFlags>();
        _featureFlags.IsEnabled(FeatureFlagCatalog.TypeScript).Returns(true);

        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(ProjectFolderPath);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        // The project root resolves a key to a path in the project folder, and the temp root to a path outside it.
        var rootHandlers = Substitute.For<IRootHandlerRegistry>();
        rootHandlers.ResolveResourcePath(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var key = callInfo.Arg<ResourceKey>().ToString();
            if (key.StartsWith("temp:"))
            {
                return Result<string>.Ok(Path.Combine(Path.GetTempPath(), key.Substring("temp:".Length)));
            }

            var relativePath = key.Replace("project:", string.Empty).Replace('/', Path.DirectorySeparatorChar);
            return Result<string>.Ok(Path.Combine(ProjectFolderPath, relativePath));
        });

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.HasWorkspaceService.Returns(true);
        workspaceWrapper.WorkspaceService.ResourceService.RootHandlers.Returns(rootHandlers);

        var appEnvironment = Substitute.For<IAppEnvironment>();
        appEnvironment.GetEnvironmentInfo().Returns(new EnvironmentInfo("1.8.0", "Windows", "Debug"));

        var definition = Substitute.For<ILanguageServerDefinition>();
        definition.Id.Returns("fake");
        definition.Feature.Returns(FeatureFlagCatalog.TypeScript);
        definition.LanguageIds.Returns(new Dictionary<string, string>
        {
            [".ts"] = "typescript",
            [".tsx"] = "typescriptreact",
        });
        definition.CreateInvocation().Returns(
            new LanguageServerInvocation("fake-server", new List<string>(), new Dictionary<string, string>()));

        var definitions = new List<ILanguageServerDefinition>
        {
            definition,
        };

        _service = new LanguageServerService(
            definitions,
            _launcher,
            new FakeLanguageServerWorkspace(),
            _featureFlags,
            projectService,
            workspaceWrapper,
            appEnvironment,
            new NullLogger<LanguageServerService>(),
            _timeProvider);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
    }

    [Test]
    public void GetServerId_ADocumentTheDefinitionCovers_ReturnsTheServersId()
    {
        var serverId = _service.GetServerId(new ResourceKey("src/main.ts"));

        serverId.Should().Be("fake");
    }

    [TestCase("notes.md")]
    [TestCase("Makefile")]
    public void GetServerId_AnotherKindOfFile_ReturnsAnEmptyString(string resourceKey)
    {
        var serverId = _service.GetServerId(new ResourceKey(resourceKey));

        serverId.Should().BeEmpty();
    }

    [Test]
    public void GetServerId_WithTheFeatureOff_ReturnsAnEmptyString()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.TypeScript).Returns(false);

        var serverId = _service.GetServerId(new ResourceKey("src/main.ts"));

        serverId.Should().BeEmpty();
    }

    [Test]
    public void GetServerId_AFileOutsideTheProjectFolder_ReturnsAnEmptyString()
    {
        var serverId = _service.GetServerId(new ResourceKey("temp:scratch.ts"));

        serverId.Should().BeEmpty();
    }

    [Test]
    public async Task ConnectPage_ADocumentWithoutAServer_RefusesThePageAndStartsNoServer()
    {
        using var page = _service.ConnectPage(new ResourceKey("notes.md"));

        var refusal = await ReadPageAsync(page);
        var lastMessage = await page.ReadNextAsync(CancellationToken.None);

        ((string?)refusal["method"]).Should().Be("celbridge/serverUnavailable");
        lastMessage.Should().BeNull();
        _launcher.Servers.Should().BeEmpty();
    }

    [Test]
    public async Task ConnectPage_TwoDocuments_ShareOneServerStartedInTheProjectFolder()
    {
        using var mainPage = _service.ConnectPage(new ResourceKey("src/main.ts"));
        using var viewPage = _service.ConnectPage(new ResourceKey("src/view.tsx"));
        await _launcher.Servers[0].CompleteInitializeAsync(new JsonObject());

        var mainReady = await ReadPageAsync(mainPage);
        var viewReady = await ReadPageAsync(viewPage);

        _launcher.Servers.Should().HaveCount(1);
        _launcher.WorkingFolderPaths.Should().Equal(ProjectFolderPath);
        ((string?)mainReady["method"]).Should().Be("celbridge/serverReady");
        ((string?)viewReady["method"]).Should().Be("celbridge/serverReady");
    }

    [Test]
    public async Task ConnectPage_AfterARename_OpensTheDocumentUnderItsNewUriAndLanguage()
    {
        var server = await StartServerAsync();
        var page = _service.ConnectPage(new ResourceKey("src/main.ts"));
        await OpenDocumentAsync(page, server);

        // A renamed document's page closes its connection, and connects again under the new name.
        page.Dispose();
        var renamedPage = _service.ConnectPage(new ResourceKey("src/renamed.tsx"));
        await ReadPageAsync(renamedPage);
        renamedPage.Receive("""{"method":"textDocument/didOpen","params":{"text":"","version":7}}""");
        var didClose = await server.ReadMethodAsync("textDocument/didClose");
        var didOpen = await server.ReadMethodAsync("textDocument/didOpen");

        ((string?)didClose["params"]!["textDocument"]!["uri"]).Should().EndWith("/src/main.ts");
        ((string?)didOpen["params"]!["textDocument"]!["uri"]).Should().EndWith("/src/renamed.tsx");
        ((string?)didOpen["params"]!["textDocument"]!["languageId"]).Should().Be("typescriptreact");
        _launcher.Servers.Should().HaveCount(1);
    }

    [Test]
    public async Task ConnectPage_AfterACrash_StartsTheServerAgain()
    {
        var server = await StartServerAsync();
        var page = _service.ConnectPage(new ResourceKey("src/main.ts"));
        await OpenDocumentAsync(page, server);

        await CrashServerAsync(server, page);
        using var secondPage = _service.ConnectPage(new ResourceKey("src/main.ts"));
        await _launcher.Servers[1].CompleteInitializeAsync(new JsonObject());
        var ready = await ReadPageAsync(secondPage);

        _launcher.Servers.Should().HaveCount(2);
        ((string?)ready["method"]).Should().Be("celbridge/serverReady");
    }

    [Test]
    public async Task ConnectPage_AfterThreeCrashesWithinAMinute_RefusesThePageAndStartsNoServer()
    {
        for (var crashIndex = 0; crashIndex < 3; crashIndex++)
        {
            var page = _service.ConnectPage(new ResourceKey("src/main.ts"));
            await CrashServerAsync(_launcher.Servers[crashIndex], page);
            _timeProvider.Advance(TimeSpan.FromSeconds(10));
        }

        using var refusedPage = _service.ConnectPage(new ResourceKey("src/main.ts"));
        var refusal = await ReadPageAsync(refusedPage);

        ((string?)refusal["method"]).Should().Be("celbridge/serverUnavailable");
        _launcher.Servers.Should().HaveCount(3);
    }

    [Test]
    public async Task ConnectPage_AfterThreeCrashesSpreadOverMinutes_StartsTheServerAgain()
    {
        for (var crashIndex = 0; crashIndex < 3; crashIndex++)
        {
            var page = _service.ConnectPage(new ResourceKey("src/main.ts"));
            await CrashServerAsync(_launcher.Servers[crashIndex], page);
            _timeProvider.Advance(TimeSpan.FromSeconds(45));
        }

        using var nextPage = _service.ConnectPage(new ResourceKey("src/main.ts"));

        _launcher.Servers.Should().HaveCount(4);
    }

    [Test]
    public async Task ConnectPage_AServerThatFailsToStart_RefusesThePage()
    {
        _launcher.FailToStart = true;

        using var page = _service.ConnectPage(new ResourceKey("src/main.ts"));
        var refusal = await ReadPageAsync(page);

        ((string?)refusal["method"]).Should().Be("celbridge/serverUnavailable");
    }

    [Test]
    public async Task Dispose_StopsTheRunningServer()
    {
        var server = await StartServerAsync();

        _service.Dispose();
        var shutdownRequest = await server.ReadMethodAsync("shutdown");
        await server.RespondAsync(shutdownRequest, null);
        await server.ReadMethodAsync("exit");
        server.Exit();
        using var page = _service.ConnectPage(new ResourceKey("src/main.ts"));
        var refusal = await ReadPageAsync(page);

        ((string?)refusal["method"]).Should().Be("celbridge/serverUnavailable");
        _launcher.Servers.Should().HaveCount(1);
    }

    // Connects a page so the server starts, and has the fake server answer the initialize request.
    private async Task<FakeLanguageServer> StartServerAsync()
    {
        var page = _service.ConnectPage(new ResourceKey("src/first.ts"));
        var server = _launcher.Servers[^1];
        await server.CompleteInitializeAsync(new JsonObject());
        await ReadPageAsync(page);

        return server;
    }

    private static async Task OpenDocumentAsync(ILanguagePageConnection page, FakeLanguageServer server)
    {
        await ReadPageAsync(page);
        page.Receive("""{"method":"textDocument/didOpen","params":{"text":"","version":1}}""");
        await server.ReadMethodAsync("textDocument/didOpen");
    }

    // Ends the server's process, and waits until the page's connection closes. The service has counted the crash
    // by then.
    private static async Task CrashServerAsync(FakeLanguageServer server, ILanguagePageConnection page)
    {
        server.Exit();

        while (await page.ReadNextAsync(CancellationToken.None).AsTask().WaitAsync(ReadTimeout) is not null)
        {
        }
    }

    private static async Task<JsonObject> ReadPageAsync(ILanguagePageConnection page)
    {
        using var timeoutSource = new CancellationTokenSource(ReadTimeout);
        var message = await page.ReadNextAsync(timeoutSource.Token);
        message.Should().NotBeNull("the page should get another message");

        return JsonNode.Parse(message!)!.AsObject();
    }

    private sealed class FakeLauncher : ILanguageServerLauncher
    {
        public List<FakeLanguageServer> Servers { get; } = new();

        public List<string> WorkingFolderPaths { get; } = new();

        public bool FailToStart { get; set; }

        public Result<ILanguageServerProcess> Start(LanguageServerInvocation invocation, string workingFolderPath)
        {
            if (FailToStart)
            {
                return Result<ILanguageServerProcess>.Fail("The executable is missing");
            }

            var server = new FakeLanguageServer();
            Servers.Add(server);
            WorkingFolderPaths.Add(workingFolderPath);

            return Result<ILanguageServerProcess>.Ok(server);
        }
    }
}
