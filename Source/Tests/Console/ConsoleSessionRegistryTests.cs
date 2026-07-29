using Celbridge.Console;
using Celbridge.Console.Services;
using Celbridge.Logging;
using Celbridge.Messaging.Services;
using Celbridge.Server;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleSessionRegistryTests
{
    private sealed class RecordingInjector : IConsoleCommandInjector
    {
        public List<string> Injected { get; } = new();

        public void InjectCommand(string text)
        {
            Injected.Add(text);
        }
    }

    private ConsoleSessionRegistry _registry = null!;

    [SetUp]
    public void Setup()
    {
        _registry = new ConsoleSessionRegistry(
            Substitute.For<ITcpTransport>(),
            new MessengerService(),
            Substitute.For<ILogger<ConsoleSessionRegistry>>());
    }

    [TearDown]
    public void TearDown()
    {
        _registry.Dispose();
    }

    private static readonly IReadOnlyList<ConsoleRunner> PythonRunners = new[]
    {
        new ConsoleRunner(new[] { ".py" }, "%run \"{script_path}\""),
    };

    private ConsoleSession RegisterConsole(string resourcePath, IReadOnlyList<ConsoleRunner> runners, IConsoleCommandInjector? injector = null)
    {
        var registration = new ConsoleRegistration(
            new ResourceKey(resourcePath),
            "python",
            string.Empty,
            runners,
            injector ?? new RecordingInjector());

        return _registry.Register(registration);
    }

    [Test]
    public void GetRunTargets_NeverConnectedConsole_IsIncluded()
    {
        RegisterConsole("scratch.console", PythonRunners);

        var targets = _registry.GetRunTargets(".py");

        targets.Should().HaveCount(1);
    }

    [Test]
    public void GetRunTargets_ConnectionLost_ExcludesTheConsole()
    {
        var session = RegisterConsole("scratch.console", PythonRunners);

        _registry.TryBindConnection(session.SessionId, connectionId: 7, out _).Should().BeTrue();
        _registry.GetRunTargets(".py").Should().HaveCount(1);

        // The REPL exits back to the shell prompt: the pty stays Ready but the runners are stale.
        _registry.OnConnectionLost(7);

        _registry.GetRunTargets(".py").Should().BeEmpty();
    }

    [Test]
    public void GetRunTargets_Reconnection_RestoresTheConsole()
    {
        var session = RegisterConsole("scratch.console", PythonRunners);

        _registry.TryBindConnection(session.SessionId, connectionId: 7, out _).Should().BeTrue();
        _registry.OnConnectionLost(7);

        // The user retypes celbridge-py in the same console; the inherited token rebinds.
        _registry.TryBindConnection(session.SessionId, connectionId: 8, out _).Should().BeTrue();

        _registry.GetRunTargets(".py").Should().HaveCount(1);
    }

    [Test]
    public void GetRunTargets_Reopen_ClearsStaleness()
    {
        var session = RegisterConsole("scratch.console", PythonRunners);

        _registry.TryBindConnection(session.SessionId, connectionId: 7, out _).Should().BeTrue();
        _registry.OnConnectionLost(7);
        _registry.GetRunTargets(".py").Should().BeEmpty();

        // Reopening replaces the session, so the fresh session is live until proven otherwise.
        RegisterConsole("scratch.console", PythonRunners);

        _registry.GetRunTargets(".py").Should().HaveCount(1);
    }

    [Test]
    public void RunScript_ConnectionLost_DoesNotInject()
    {
        var injector = new RecordingInjector();
        var session = RegisterConsole("scratch.console", PythonRunners, injector);

        _registry.TryBindConnection(session.SessionId, connectionId: 7, out _).Should().BeTrue();
        _registry.OnConnectionLost(7);

        _registry.RunScript(session.SessionId, "script.py", string.Empty);

        injector.Injected.Should().BeEmpty();
    }

    [Test]
    public void RunScript_LiveConnection_InjectsTheRunnerCommand()
    {
        var injector = new RecordingInjector();
        var session = RegisterConsole("scratch.console", PythonRunners, injector);

        _registry.TryBindConnection(session.SessionId, connectionId: 7, out _).Should().BeTrue();

        _registry.RunScript(session.SessionId, "script.py", string.Empty);

        injector.Injected.Should().Equal("%run \"script.py\"");
    }
}
