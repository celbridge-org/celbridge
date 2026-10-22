using Celbridge.Console.Helpers;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.Console;

/// <summary>
/// Tests for ConsoleSubmitQueue. Each submission writes the submission's text, then the submit key a beat later. The
/// next submission starts only after the previous submit key. The time moves only when a test advances it.
/// </summary>
[TestFixture]
public class ConsoleSubmitQueueTests
{
    private static readonly TimeSpan SubmitKeyDelay = TimeSpan.FromMilliseconds(100);

    private FakeTimeProvider _timeProvider = null!;
    private List<string> _writes = null!;
    private ConsoleSubmitQueue _queue = null!;

    [SetUp]
    public void Setup()
    {
        _timeProvider = new FakeTimeProvider();
        _writes = new List<string>();
        _queue = new ConsoleSubmitQueue(text => _writes.Add(text), Substitute.For<ILogger>(), _timeProvider);
    }

    [Test]
    public void ASubmissionWritesItsTextThenTheSubmitKeyABeatLater()
    {
        _queue.Enqueue("first");

        _writes.Should().Equal("first");

        _timeProvider.Advance(SubmitKeyDelay);

        _writes.Should().Equal("first", "\r");
    }

    [Test]
    public void ASubmissionWaitsForThePreviousSubmitKey()
    {
        // Two triggers due at the same moment. The second's text, written before the first's submit key, would clear
        // the first from the prompt.
        _queue.Enqueue("first");
        _queue.Enqueue("second");

        _writes.Should().Equal("first");

        _timeProvider.Advance(SubmitKeyDelay);
        _writes.Should().Equal("first", "\r", "second");

        _timeProvider.Advance(SubmitKeyDelay);
        _writes.Should().Equal("first", "\r", "second", "\r");
    }

    [Test]
    public void AStoppedQueueWritesNothingMore()
    {
        _queue.Enqueue("first");
        _queue.Enqueue("second");

        _queue.Stop();
        _timeProvider.Advance(SubmitKeyDelay);
        _timeProvider.Advance(SubmitKeyDelay);

        _writes.Should().Equal("first");
    }

    [Test]
    public void AFailedWriteLeavesTheNextSubmissionToRun()
    {
        var queue = new ConsoleSubmitQueue(
            text =>
            {
                if (text == "broken")
                {
                    throw new IOException("The terminal refused the write");
                }
                _writes.Add(text);
            },
            Substitute.For<ILogger>(),
            _timeProvider);

        queue.Enqueue("broken");
        queue.Enqueue("next");
        _timeProvider.Advance(SubmitKeyDelay);

        _writes.Should().Equal("next", "\r");
    }
}
