using Celbridge.Console;
using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class StartupInjectorTests
{
    private sealed class FakeTerminal : ITerminal
    {
        private readonly object _lock = new();
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

    private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    [Test]
    public async Task Injects_AfterOutputSettles()
    {
        var terminal = new FakeTerminal();
        using var injector = StartupInjector.Begin(terminal, new[] { "celbridge-py --python 3.13" });

        terminal.RaiseOutput("prompt> ");

        var injected = await WaitForAsync(() => terminal.Writes.Count == 1, 2000);

        injected.Should().BeTrue();
        terminal.Writes[0].Should().Be("celbridge-py --python 3.13\r");
    }

    [Test]
    public async Task Injects_AtCapWhenNoOutputArrives()
    {
        var terminal = new FakeTerminal();
        using var injector = StartupInjector.Begin(terminal, new[] { "cmd" });

        var injected = await WaitForAsync(() => terminal.Writes.Count == 1, 3000);

        injected.Should().BeTrue();
    }

    [Test]
    public async Task Injects_ExactlyOnce()
    {
        var terminal = new FakeTerminal();
        using var injector = StartupInjector.Begin(terminal, new[] { "cmd" });

        terminal.RaiseOutput("a");

        await WaitForAsync(() => terminal.Writes.Count >= 1, 2000);
        await Task.Delay(300);

        terminal.Writes.Count.Should().Be(1);
    }

    [Test]
    public async Task Dispose_BeforeSettle_PreventsInjection()
    {
        var terminal = new FakeTerminal();
        var injector = StartupInjector.Begin(terminal, new[] { "cmd" });
        injector.Dispose();

        await Task.Delay(2000);

        terminal.Writes.Should().BeEmpty();
    }

    [Test]
    public async Task Injects_EveryLineInOrder()
    {
        var terminal = new FakeTerminal();
        var lines = new[] { "celbridge-py", "import numpy as np", "%load_ext autoreload" };
        using var injector = StartupInjector.Begin(terminal, lines);

        terminal.RaiseOutput("prompt> ");

        var injected = await WaitForAsync(() => terminal.Writes.Count == 3, 2000);

        injected.Should().BeTrue();
        terminal.Writes.Should().Equal(
            "celbridge-py\r",
            "import numpy as np\r",
            "%load_ext autoreload\r");
    }

    [Test]
    public async Task Callback_FiresAfterInjection()
    {
        var terminal = new FakeTerminal();
        var callbackFired = false;
        using var injector = StartupInjector.Begin(terminal, new[] { "cmd" }, () => callbackFired = true);

        terminal.RaiseOutput("prompt> ");

        var injected = await WaitForAsync(() => callbackFired, 2000);

        injected.Should().BeTrue();
        terminal.Writes.Count.Should().Be(1);
    }

    [Test]
    public async Task Callback_SkippedWhenDisposedBeforeInjection()
    {
        var terminal = new FakeTerminal();
        var callbackFired = false;
        var injector = StartupInjector.Begin(terminal, new[] { "cmd" }, () => callbackFired = true);
        injector.Dispose();

        await Task.Delay(2000);

        callbackFired.Should().BeFalse();
    }
}
