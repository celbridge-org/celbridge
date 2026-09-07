using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class PendingViewSizeTests
{
    // Long enough that a wait which should complete never races the timeout, short enough that the tests
    // which do time out stay quick.
    private const int WaitTimeoutMs = 2000;
    private const int TimeoutTestMs = 50;

    [Test]
    public async Task WaitAsync_ReturnsASizeReportedBeforeTheWait()
    {
        var pendingViewSize = new PendingViewSize();
        pendingViewSize.Report(75, 26);

        var size = await pendingViewSize.WaitAsync(WaitTimeoutMs);

        size.Should().Be(new TerminalSize(75, 26));
    }

    [Test]
    public async Task WaitAsync_ReturnsASizeReportedWhileWaiting()
    {
        var pendingViewSize = new PendingViewSize();

        var waiting = pendingViewSize.WaitAsync(WaitTimeoutMs);
        pendingViewSize.Report(120, 30);

        (await waiting).Should().Be(new TerminalSize(120, 30));
    }

    [Test]
    public async Task WaitAsync_ReturnsNull_WhenNoSizeIsReportedInTime()
    {
        var pendingViewSize = new PendingViewSize();

        var size = await pendingViewSize.WaitAsync(TimeoutTestMs);

        size.Should().BeNull("a launch that is never told a size has to fall back to its own");
    }

    [Test]
    public async Task Report_IgnoresAnEmptySize()
    {
        var pendingViewSize = new PendingViewSize();

        // The size a view reports before it has been arranged. Creating a pty at it would collapse the
        // terminal, so it must not satisfy the wait.
        pendingViewSize.Report(0, 0);

        var size = await pendingViewSize.WaitAsync(TimeoutTestMs);

        size.Should().BeNull();
    }

    [TestCase(0, 26)]
    [TestCase(75, 0)]
    [TestCase(-1, 26)]
    [TestCase(75, -1)]
    public async Task Report_IgnoresASizeWithANonPositiveDimension(int cols, int rows)
    {
        var pendingViewSize = new PendingViewSize();
        pendingViewSize.Report(cols, rows);

        var size = await pendingViewSize.WaitAsync(TimeoutTestMs);

        size.Should().BeNull();
    }

    [Test]
    public async Task Report_KeepsTheFirstSize_WhenAViewReportsAgain()
    {
        var pendingViewSize = new PendingViewSize();

        pendingViewSize.Report(75, 26);
        pendingViewSize.Report(100, 40);

        var size = await pendingViewSize.WaitAsync(WaitTimeoutMs);

        size.Should().Be(
            new TerminalSize(75, 26),
            "the launch is waiting for a size to build the pty with, not for the latest one");
    }

    [Test]
    public async Task Report_SatisfiesAWaitThatAnEmptySizeLeftPending()
    {
        var pendingViewSize = new PendingViewSize();

        var waiting = pendingViewSize.WaitAsync(WaitTimeoutMs);
        pendingViewSize.Report(0, 0);
        pendingViewSize.Report(75, 26);

        (await waiting).Should().Be(new TerminalSize(75, 26));
    }
}
