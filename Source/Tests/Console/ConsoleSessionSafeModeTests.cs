using Celbridge.Console;
using Celbridge.Console.Services;
using Celbridge.Messaging;
using Celbridge.Python;
using Celbridge.Server;
using Celbridge.Tests.Helpers;
using Celbridge.WebHost;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Console;

/// <summary>
/// Covers the two cases where the console session service refuses to start a session. The first is Safe Mode,
/// in any editor that shows the console file. The second is a call after the workspace unloads.
/// </summary>
[TestFixture]
public class ConsoleSessionSafeModeTests
{
    private IServiceProvider? _previousServiceProvider;
    private ITcpTransport _tcpTransport = null!;

    [SetUp]
    public void Setup()
    {
        _previousServiceProvider = ServiceLocator.ServiceProvider;
        _tcpTransport = Substitute.For<ITcpTransport>();
    }

    [TearDown]
    public void TearDown()
    {
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
    public async Task InSafeMode_AnAttachReportsTheSessionUnavailable()
    {
        var sessionService = CreateSessionService(CreateSafeModeWorkspace());

        var snapshot = await AttachAsync(sessionService);

        snapshot.State.Should().Be(ConsoleSessionRunState.Failed);
    }

    [Test]
    public async Task InSafeMode_EnsureStarted_StartsNoSession()
    {
        var sessionService = CreateSessionService(CreateSafeModeWorkspace());

        await sessionService.EnsureStartedAsync(new ResourceKey("terminal.console"));

        // A session launch opens the shared listener first. So in Safe Mode the listener stays closed.
        await _tcpTransport.DidNotReceiveWithAnyArgs().StartListeningAsync(default, default);
    }

    [Test]
    public async Task AnAttachAfterTheWorkspaceUnloads_ReportsTheSessionUnavailable()
    {
        // The workspace dispose disposes the service. The wrapper then loses the workspace service.
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(_ => throw new InvalidOperationException("No workspace is loaded"));

        var sessionService = CreateSessionService(workspaceWrapper);
        sessionService.Dispose();

        var snapshot = await AttachAsync(sessionService);

        snapshot.State.Should().Be(ConsoleSessionRunState.Failed);
    }

    private static IWorkspaceWrapper CreateSafeModeWorkspace()
    {
        // Built before the Returns call, because building the service sets up substitutes of its own.
        var safeModeService = TestSafeMode.Create(isActive: true);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);

        return workspaceWrapper;
    }

    private ConsoleSessionService CreateSessionService(IWorkspaceWrapper workspaceWrapper)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWebViewPlatform>());
        services.AddSingleton(Substitute.For<IPythonInstaller>());
        services.AddSingleton(_tcpTransport);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var serviceProvider = services.BuildServiceProvider();

        // The service acquires the WebView platform through the global ServiceLocator. The fixture restores the
        // ServiceLocator after each test.
        ServiceLocator.Initialize(serviceProvider);

        return new ConsoleSessionService(
            serviceProvider,
            workspaceWrapper,
            Substitute.For<IMessengerService>(),
            new NullLogger<ConsoleSessionService>());
    }

    private static Task<ConsoleAttachSnapshot> AttachAsync(ConsoleSessionService sessionService)
    {
        return sessionService.AttachAsync(
            new ResourceKey("terminal.console"),
            Substitute.For<IConsoleView>(),
            cols: 80,
            rows: 24);
    }
}
