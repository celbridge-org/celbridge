using Celbridge.Console;
using Celbridge.Console.Services;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.Console;

[TestFixture]
public class StartupInjectorTests
{
    // The injector polls the shell's output at this interval. The shell counts as settled once its output has
    // been quiet for 150 ms, or once 1.5 s have passed with no output at all.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private sealed class FakeTerminal : ITerminal
    {
        private readonly Lock _lock = new();
        private readonly List<string> _writes = new();

        public event EventHandler<string>? OutputReceived;
        public event EventHandler? ProcessExited;

        public int? ProcessId => null;

        public IReadOnlyList<string> Writes
        {
            get
            {
                lock (_lock)
                {
                    return _writes.ToList();
                }
            }
        }

        public void Start(string commandLine, string workingDir, Dictionary<string, string>? environmentVariables = null)
        {
        }

        public void Write(string input)
        {
            lock (_lock)
            {
                _writes.Add(input);
            }
        }

        public void SetSize(int cols, int rows)
        {
        }

        public void RaiseOutput(string text)
        {
            OutputReceived?.Invoke(this, text);
        }

        public void RaiseProcessExited()
        {
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
        }
    }

    [Test]
    public void Injects_AfterOutputSettles()
    {
        var terminal = new FakeTerminal();
        var clock = new FakeTimeProvider();
        var lines = new[] { "celbridge-py --python 3.13" };
        using var injector = StartupInjector.Begin(terminal, lines, timeProvider: clock);

        terminal.RaiseOutput("prompt> ");
        AdvanceInPolls(clock, TimeSpan.FromMilliseconds(125));
        terminal.Writes.Should().BeEmpty();

        AdvanceInPolls(clock, PollInterval);
        terminal.Writes.Should().Equal("celbridge-py --python 3.13\r");
    }

    [Test]
    public void Injects_AtCapWhenNoOutputArrives()
    {
        var terminal = new FakeTerminal();
        var clock = new FakeTimeProvider();
        using var injector = StartupInjector.Begin(terminal, new[] { "cmd" }, timeProvider: clock);

        AdvanceInPolls(clock, TimeSpan.FromMilliseconds(1475));
        terminal.Writes.Should().BeEmpty();

        AdvanceInPolls(clock, PollInterval);
        terminal.Writes.Should().Equal("cmd\r");
    }

    [Test]
    public void Injects_ExactlyOnce()
    {
        var terminal = new FakeTerminal();
        var clock = new FakeTimeProvider();
        using var injector = StartupInjector.Begin(terminal, new[] { "cmd" }, timeProvider: clock);

        terminal.RaiseOutput("a");
        AdvanceInPolls(clock, TimeSpan.FromSeconds(3));

        terminal.Writes.Should().Equal("cmd\r");
    }

    [Test]
    public void Dispose_BeforeSettle_PreventsInjection()
    {
        var terminal = new FakeTerminal();
        var clock = new FakeTimeProvider();
        var injector = StartupInjector.Begin(terminal, new[] { "cmd" }, timeProvider: clock);

        injector.Dispose();
        AdvanceInPolls(clock, TimeSpan.FromSeconds(3));

        terminal.Writes.Should().BeEmpty();
    }

    [Test]
    public void Injects_EveryLineInOrder()
    {
        var terminal = new FakeTerminal();
        var clock = new FakeTimeProvider();
        var lines = new[] { "celbridge-py", "import numpy as np", "%load_ext autoreload" };
        using var injector = StartupInjector.Begin(terminal, lines, timeProvider: clock);

        terminal.RaiseOutput("prompt> ");
        AdvanceInPolls(clock, TimeSpan.FromMilliseconds(150));

        terminal.Writes.Should().Equal(
            "celbridge-py\r",
            "import numpy as np\r",
            "%load_ext autoreload\r");
    }

    [Test]
    public void Callback_FiresAfterInjection()
    {
        var terminal = new FakeTerminal();
        var clock = new FakeTimeProvider();
        var callbackFired = false;
        using var injector = StartupInjector.Begin(terminal, new[] { "cmd" }, () => callbackFired = true, clock);

        terminal.RaiseOutput("prompt> ");
        AdvanceInPolls(clock, TimeSpan.FromMilliseconds(150));

        callbackFired.Should().BeTrue();
        terminal.Writes.Count.Should().Be(1);
    }

    [Test]
    public void Callback_SkippedWhenDisposedBeforeInjection()
    {
        var terminal = new FakeTerminal();
        var clock = new FakeTimeProvider();
        var callbackFired = false;
        var injector = StartupInjector.Begin(terminal, new[] { "cmd" }, () => callbackFired = true, clock);

        injector.Dispose();
        AdvanceInPolls(clock, TimeSpan.FromSeconds(3));

        callbackFired.Should().BeFalse();
    }

    // Advances the clock one poll interval at a time. Each step fires the pending poll, and that poll starts the
    // next one.
    private static void AdvanceInPolls(FakeTimeProvider clock, TimeSpan duration)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < duration; elapsed += PollInterval)
        {
            clock.Advance(PollInterval);
        }
    }
}
