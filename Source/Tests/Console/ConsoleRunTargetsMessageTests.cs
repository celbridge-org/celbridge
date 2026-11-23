using Celbridge.Console;
using Celbridge.Console.Services;
using Celbridge.Documents;
using Celbridge.Localization;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Python;
using Celbridge.Server;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Localization;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Console;

/// <summary>
/// Covers the message that says the Run menu's targets may have changed. The service sends the message after four
/// changes: a launch, a client's connection, a lost connection and a released session. The launch in these tests
/// stops at the provider, so no shell starts.
/// </summary>
[TestFixture]
public class ConsoleRunTargetsMessageTests
{
    private const int ConnectionId = 7;

    private static readonly ResourceKey ConsoleResource = new("python.console");

    private IServiceProvider? _previousServiceProvider;
    private IMessengerService _messengerService = null!;
    private ITemporaryEnvironments _temporaryEnvironments = null!;
    private ServiceProvider _serviceProvider = null!;
    private ConsoleSessionService _sessionService = null!;
    private Guid _sessionToken;

    [SetUp]
    public void Setup()
    {
        _previousServiceProvider = ServiceLocator.ServiceProvider;

        _messengerService = Substitute.For<IMessengerService>();
        _temporaryEnvironments = Substitute.For<ITemporaryEnvironments>();

        // A substitute returns false for every flag a test has not set up. Every launch here needs consoles enabled.
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(FeatureFlagCatalog.Consoles).Returns(true);

        // The launch gives the provider the session's token, then stops at the provider's failure.
        var pythonProvider = Substitute.For<IConsoleSessionProvider>();
        pythonProvider.SessionType.Returns(
            new ConsoleSessionType("python", Array.Empty<string>(), Array.Empty<ConsoleRunner>()));
        pythonProvider.BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>())
            .Returns(callInfo =>
            {
                var context = callInfo.Arg<ConsoleSessionContext>();
                _sessionToken = Guid.Parse(context.Environment[ConsoleEnvironmentVariables.SessionToken]);

                return Result<ConsoleStartupInvocation>.Fail("The provider ran");
            });

        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(OperatingSystem.IsWindows() ? @"C:\Projects\Demo" : "/projects/demo");
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWebViewPlatform>());
        services.AddSingleton(_temporaryEnvironments);
        services.AddSingleton(_messengerService);
        services.AddSingleton(Substitute.For<ITcpTransport>());
        services.AddSingleton(featureFlags);
        services.AddSingleton<ILocalizerService>(new TestLocalizerService());
        services.AddSingleton(projectService);
        services.AddSingleton(pythonProvider);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _serviceProvider = services.BuildServiceProvider();

        // The service acquires the WebView platform through the global ServiceLocator. TearDown restores it.
        ServiceLocator.Initialize(_serviceProvider);

        _sessionService = new ConsoleSessionService(
            _serviceProvider,
            CreateWorkspace(),
            _messengerService,
            new NullLogger<ConsoleSessionService>());
    }

    [TearDown]
    public void TearDown()
    {
        _sessionService.Dispose();
        _serviceProvider.Dispose();

        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
    }

    [Test]
    public async Task ALaunch_SendsTheMessage()
    {
        await LaunchAsync();

        ReceivedMessageCount().Should().Be(1);
    }

    [Test]
    public async Task AClientThatConnects_SendsTheMessage()
    {
        await LaunchAsync();
        _messengerService.ClearReceivedCalls();

        var isBound = _sessionService.TryBindConnection(_sessionToken, ConnectionId, null);

        isBound.Should().BeTrue();
        ReceivedMessageCount().Should().Be(1);
    }

    [Test]
    public async Task AClientWithAnUnknownToken_SendsNoMessage()
    {
        await LaunchAsync();
        _messengerService.ClearReceivedCalls();

        var isBound = _sessionService.TryBindConnection(Guid.NewGuid(), ConnectionId, null);

        isBound.Should().BeFalse();
        ReceivedMessageCount().Should().Be(0);
    }

    [Test]
    public async Task ALostConnection_SendsTheMessage()
    {
        await LaunchAsync();
        _sessionService.TryBindConnection(_sessionToken, ConnectionId, null);
        _messengerService.ClearReceivedCalls();

        _sessionService.OnConnectionLost(ConnectionId);

        ReceivedMessageCount().Should().Be(1);
    }

    [Test]
    public async Task ARenamedConsole_SendsTheMessage()
    {
        await LaunchAsync();
        var renameHandler = _messengerService.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .OfType<MessageHandler<object, DocumentResourceChangedMessage>>()
            .Single();
        _messengerService.ClearReceivedCalls();

        renameHandler(this, new DocumentResourceChangedMessage(ConsoleResource, new ResourceKey("renamed.console")));

        ReceivedMessageCount().Should().Be(1);
    }

    [Test]
    public async Task AReleasedSession_SendsTheMessage()
    {
        await LaunchAsync();
        _messengerService.ClearReceivedCalls();

        _sessionService.EndSession(ConsoleResource);

        ReceivedMessageCount().Should().Be(1);
    }

    [Test]
    public async Task AReleasedSession_RemovesTheEnvironmentItsClientReported()
    {
        await LaunchAsync();
        _sessionService.TryBindConnection(_sessionToken, ConnectionId, "/cache/first");
        var removed = WaitForRemove();

        _sessionService.EndSession(ConsoleResource);

        var folderPath = await removed.WaitAsync(TimeSpan.FromSeconds(5));
        folderPath.Should().Be("/cache/first");
    }

    [Test]
    public async Task ASecondClientThatReportsNoEnvironment_ClearsTheFirstClientsEnvironment()
    {
        await LaunchAsync();
        _sessionService.TryBindConnection(_sessionToken, ConnectionId, "/cache/first");
        _sessionService.OnConnectionLost(ConnectionId);
        _sessionService.TryBindConnection(_sessionToken, ConnectionId + 1, null);
        var removed = WaitForRemove();

        _sessionService.EndSession(ConsoleResource);

        // A removal starts within a few milliseconds of the release, so this wait would see one.
        var finished = await Task.WhenAny(removed, Task.Delay(TimeSpan.FromMilliseconds(500)));
        finished.Should().NotBe(removed);
    }

    private Task<string> WaitForRemove()
    {
        var removed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _temporaryEnvironments.RemoveAsync(Arg.Any<string>())
            .Returns(callInfo =>
            {
                removed.TrySetResult(callInfo.Arg<string>());

                return Task.CompletedTask;
            });

        return removed.Task;
    }

    private int ReceivedMessageCount()
    {
        var count = _messengerService.ReceivedCalls()
            .Count(call => call.GetArguments().Any(argument => argument is ConsoleRunTargetsChangedMessage));

        return count;
    }

    private static IWorkspaceWrapper CreateWorkspace()
    {
        // The Safe Mode service is built before the Returns calls, because its builder sets up more substitutes.
        var safeModeService = TestSafeMode.Create(isActive: false);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);
        workspaceWrapper.WorkspaceService.ResourceService.FileSystem.ReadAllTextAsync(ConsoleResource)
            .Returns(Result<string>.Ok("[session]\ntype = \"python\"\n"));

        return workspaceWrapper;
    }

    private Task<ConsoleAttachSnapshot> LaunchAsync()
    {
        return _sessionService.AttachAsync(ConsoleResource, Substitute.For<IConsoleView>(), cols: 80, rows: 24);
    }
}
