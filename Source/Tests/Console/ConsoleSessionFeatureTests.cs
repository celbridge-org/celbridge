using Celbridge.Console;
using Celbridge.Console.Services;
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
/// Covers the features a console needs. A console starts only while the loaded project has consoles enabled, and
/// the feature its type needs, if any. While either is disabled, the service still reports the type, marked as
/// unavailable.
/// </summary>
[TestFixture]
public class ConsoleSessionFeatureTests
{
    private static readonly ResourceKey PythonConsoleResource = new("python.console");
    private static readonly ResourceKey ShellConsoleResource = new("shell.console");

    private IServiceProvider? _previousServiceProvider;
    private IFeatureFlags _featureFlags = null!;
    private IConsoleSessionProvider _shellProvider = null!;
    private IConsoleSessionProvider _pythonProvider = null!;
    private ITcpTransport _tcpTransport = null!;
    private ServiceProvider _serviceProvider = null!;
    private ConsoleSessionService _sessionService = null!;

    [SetUp]
    public void Setup()
    {
        _previousServiceProvider = ServiceLocator.ServiceProvider;

        // A substitute returns false for every flag a test has not set up, so the setup enables each flag the tests
        // need.
        _featureFlags = Substitute.For<IFeatureFlags>();
        _featureFlags.IsEnabled(FeatureFlagCatalog.Consoles).Returns(true);
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(true);

        _shellProvider = CreateProvider("shell", feature: null);
        _pythonProvider = CreateProvider("python", FeatureFlagCatalog.Python);

        // The launch stops at the provider's failure, so a test never reaches the shell.
        _pythonProvider.BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>())
            .Returns(Result<ConsoleStartupInvocation>.Fail("The provider ran"));

        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(OperatingSystem.IsWindows() ? @"C:\Projects\Demo" : "/projects/demo");
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        _tcpTransport = Substitute.For<ITcpTransport>();

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWebViewPlatform>());
        services.AddSingleton(Substitute.For<ITemporaryEnvironments>());
        services.AddSingleton(Substitute.For<IMessengerService>());
        services.AddSingleton(_tcpTransport);
        services.AddSingleton(_featureFlags);
        services.AddSingleton<ILocalizerService>(new TestLocalizerService());
        services.AddSingleton(projectService);
        services.AddSingleton(_shellProvider);
        services.AddSingleton(_pythonProvider);
        services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));
        _serviceProvider = services.BuildServiceProvider();

        // The service acquires the WebView platform through the global ServiceLocator. TearDown restores it.
        ServiceLocator.Initialize(_serviceProvider);

        _sessionService = new ConsoleSessionService(
            _serviceProvider,
            CreateWorkspace(),
            Substitute.For<IMessengerService>(),
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
    public async Task FeatureDisabled_TheConsoleFailsWithTheDisabledNotice()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        var snapshot = await AttachAsync(PythonConsoleResource);

        snapshot.State.Should().Be(ConsoleSessionRunState.Failed);
        snapshot.Error.Should().Be(FeatureDisabledNotice());
    }

    [Test]
    public async Task FeatureDisabled_TheProviderNeverRuns()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        await AttachAsync(PythonConsoleResource);

        await _pythonProvider.DidNotReceive().BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>());
    }

    [Test]
    public async Task FeatureDisabled_TheConsoleLogsNoWarning()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        await AttachAsync(PythonConsoleResource);

        // The project chose to disable the feature, so the console is working as expected.
        SessionLogEntries().Should().NotContain(entry => entry.Level == LogEntryLevel.Warning);
    }

    [Test]
    public async Task FeatureEnabled_TheProviderBuildsTheStartup()
    {
        var snapshot = await AttachAsync(PythonConsoleResource);

        await _pythonProvider.Received(1).BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>());
        snapshot.Error.Should().Be("The provider ran");
    }

    [Test]
    public async Task FeatureEnabled_TheProxyListenerStarts()
    {
        await AttachAsync(PythonConsoleResource);

        ProxyListenerStarted().Should().BeTrue();
    }

    [Test]
    public async Task FeatureDisabled_TheProxyListenerNeverStarts()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        await AttachAsync(PythonConsoleResource);

        ProxyListenerStarted().Should().BeFalse();
    }

    [Test]
    public async Task ConsolesDisabled_TheProxyListenerNeverStarts()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Consoles).Returns(false);

        await AttachAsync(ShellConsoleResource);

        ProxyListenerStarted().Should().BeFalse();
    }

    [Test]
    public async Task ConsolesDisabled_AShellConsoleFailsWithTheNotice()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Consoles).Returns(false);

        var snapshot = await AttachAsync(ShellConsoleResource);

        snapshot.State.Should().Be(ConsoleSessionRunState.Failed);
        snapshot.Error.Should().Be(FeatureDisabledNotice());
    }

    [Test]
    public async Task ConsolesDisabled_TheShellProviderNeverRuns()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Consoles).Returns(false);

        await AttachAsync(ShellConsoleResource);

        await _shellProvider.DidNotReceive().BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>());
    }

    [Test]
    public async Task ConsolesDisabled_APythonConsoleFailsWithTheNoticeWhilePythonIsEnabled()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Consoles).Returns(false);

        var snapshot = await AttachAsync(PythonConsoleResource);

        snapshot.State.Should().Be(ConsoleSessionRunState.Failed);
        snapshot.Error.Should().Be(FeatureDisabledNotice());
        await _pythonProvider.DidNotReceive().BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>());
    }

    [Test]
    public void GetSessionTypes_ListsATypeWhoseFeatureIsDisabled()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        var typeIds = _sessionService.GetSessionTypes()
            .Select(sessionType => sessionType.TypeId)
            .ToList();

        typeIds.Should().Equal("shell", "python");
    }

    [Test]
    public void GetSessionTypes_MarksATypeWhoseFeatureIsDisabledAsUnavailable()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        var availability = Availability();

        availability["shell"].Should().BeTrue();
        availability["python"].Should().BeFalse();
    }

    [Test]
    public void GetSessionTypes_MarksEveryTypeAvailableWhileTheFeatureIsEnabled()
    {
        var availability = Availability();

        availability.Values.Should().AllSatisfy(isAvailable => isAvailable.Should().BeTrue());
    }

    [Test]
    public void GetSessionTypes_MarksEveryTypeUnavailableWhileConsolesAreDisabled()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Consoles).Returns(false);

        var availability = Availability();

        availability.Should().HaveCount(2);
        availability.Values.Should().AllSatisfy(isAvailable => isAvailable.Should().BeFalse());
    }

    private IReadOnlyDictionary<string, bool> Availability()
    {
        var availability = _sessionService.GetSessionTypes()
            .ToDictionary(
                sessionType => sessionType.TypeId,
                sessionType => sessionType.IsAvailable);

        return availability;
    }

    private static IConsoleSessionProvider CreateProvider(string typeId, FeatureFlagDescriptor? feature)
    {
        var provider = Substitute.For<IConsoleSessionProvider>();
        provider.SessionType.Returns(new ConsoleSessionType(typeId, Array.Empty<string>(), Array.Empty<ConsoleRunner>()));
        provider.Feature.Returns(feature);

        return provider;
    }

    private static IWorkspaceWrapper CreateWorkspace()
    {
        // The Safe Mode service is built before the Returns calls, because its builder sets up more substitutes.
        var safeModeService = TestSafeMode.Create(isActive: false);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.SafeModeService.Returns(safeModeService);
        workspaceWrapper.WorkspaceService.ResourceService.FileSystem.ReadAllTextAsync(PythonConsoleResource)
            .Returns(Result<string>.Ok("[session]\ntype = \"python\"\n"));
        workspaceWrapper.WorkspaceService.ResourceService.FileSystem.ReadAllTextAsync(ShellConsoleResource)
            .Returns(Result<string>.Ok("[session]\ntype = \"shell\"\n"));

        return workspaceWrapper;
    }

    private Task<ConsoleAttachSnapshot> AttachAsync(ResourceKey consoleResource)
    {
        return _sessionService.AttachAsync(consoleResource, Substitute.For<IConsoleView>(), cols: 80, rows: 24);
    }

    private static string FeatureDisabledNotice()
    {
        return new TestLocalizerService().GetString("Console_FeatureDisabled");
    }

    // ConsoleProxyListener.EnsureStarted registers a handshake target on the transport.
    private bool ProxyListenerStarted()
    {
        var registrations = _tcpTransport.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ITcpTransport.AddRpcTargetFactory));

        return registrations > 0;
    }

    // ConsoleSession is internal to the console assembly, so its logger is found by the session's type name.
    private IReadOnlyList<LogEntry> SessionLogEntries()
    {
        var consoleAssembly = typeof(ConsoleSessionService).Assembly;
        var sessionType = consoleAssembly.GetType("Celbridge.Console.Services.ConsoleSession")!;
        var loggerType = typeof(ILogger<>).MakeGenericType(sessionType);
        var logger = _serviceProvider.GetRequiredService(loggerType);

        var entriesProperty = logger.GetType().GetProperty(nameof(RecordingLogger<object>.Entries))!;
        var entries = (IReadOnlyList<LogEntry>)entriesProperty.GetValue(logger)!;

        return entries;
    }
}
