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
/// Covers a session type that needs a project feature. A console of the type starts only while the loaded project
/// has the feature enabled. While the feature is disabled, the service leaves the type out of the types it reports.
/// </summary>
[TestFixture]
public class ConsoleSessionFeatureTests
{
    private static readonly ResourceKey ConsoleResource = new("python.console");

    private IServiceProvider? _previousServiceProvider;
    private IFeatureFlags _featureFlags = null!;
    private IConsoleSessionProvider _shellProvider = null!;
    private IConsoleSessionProvider _pythonProvider = null!;
    private ServiceProvider _serviceProvider = null!;
    private ConsoleSessionService _sessionService = null!;

    [SetUp]
    public void Setup()
    {
        _previousServiceProvider = ServiceLocator.ServiceProvider;

        _featureFlags = Substitute.For<IFeatureFlags>();
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

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IWebViewPlatform>());
        services.AddSingleton(Substitute.For<ITemporaryEnvironments>());
        services.AddSingleton(Substitute.For<ITcpTransport>());
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
    public async Task FeatureOff_TheConsoleFailsWithTheTypesNotice()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        var snapshot = await AttachAsync();

        snapshot.State.Should().Be(ConsoleSessionRunState.Failed);
        snapshot.Error.Should().Be(new TestLocalizerService().GetString("Console_Disabled_Python"));
    }

    [Test]
    public async Task FeatureOff_TheProviderNeverRuns()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        await AttachAsync();

        await _pythonProvider.DidNotReceive().BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>());
    }

    [Test]
    public async Task FeatureOff_TheConsoleLogsNoWarning()
    {
        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        await AttachAsync();

        // The project chose to disable the feature, so the console is working as expected.
        SessionLogEntries().Should().NotContain(entry => entry.Level == LogEntryLevel.Warning);
    }

    [Test]
    public async Task FeatureOn_TheProviderBuildsTheStartup()
    {
        var snapshot = await AttachAsync();

        await _pythonProvider.Received(1).BuildStartupInvocationAsync(Arg.Any<ConsoleSessionContext>());
        snapshot.Error.Should().Be("The provider ran");
    }

    [Test]
    public void GetSessionTypes_LeavesOutATypeWhoseFeatureIsDisabled()
    {
        SessionTypeIds().Should().Equal("shell", "python");

        _featureFlags.IsEnabled(FeatureFlagCatalog.Python).Returns(false);

        SessionTypeIds().Should().Equal("shell");
    }

    private IReadOnlyList<string> SessionTypeIds()
    {
        var typeIds = _sessionService.GetSessionTypes()
            .Select(sessionType => sessionType.TypeId)
            .ToList();

        return typeIds;
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
        workspaceWrapper.WorkspaceService.ResourceService.FileSystem.ReadAllTextAsync(ConsoleResource)
            .Returns(Result<string>.Ok("[session]\ntype = \"python\"\n"));

        return workspaceWrapper;
    }

    private Task<ConsoleAttachSnapshot> AttachAsync()
    {
        return _sessionService.AttachAsync(ConsoleResource, Substitute.For<IConsoleView>(), cols: 80, rows: 24);
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
