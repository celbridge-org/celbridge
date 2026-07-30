using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleOutputBufferTests
{
    [Test]
    public void Snapshot_ReturnsAppendedTextInOrder()
    {
        var buffer = new ConsoleOutputBuffer();

        buffer.Append("one ");
        buffer.Append("two");

        buffer.Snapshot().Should().Be("one two");
    }

    [Test]
    public void Snapshot_EmptyBuffer_IsEmpty()
    {
        new ConsoleOutputBuffer().Snapshot().Should().BeEmpty();
    }

    [Test]
    public void Append_OverCapacity_DropsTheOldestContentAtALineBreak()
    {
        var buffer = new ConsoleOutputBuffer();
        var line = new string('x', 999) + "\n";

        // Push well past the cap, then confirm the retained content is the newest and starts on a line.
        for (var index = 0; index < 400; index++)
        {
            buffer.Append(index.ToString("D4") + line);
        }

        var snapshot = buffer.Snapshot();
        snapshot.Length.Should().BeLessThan(300_000);
        snapshot.Should().EndWith("0399" + line);
        snapshot.Should().NotContain("0000");
    }

    [Test]
    public void Clear_EmptiesTheBuffer()
    {
        var buffer = new ConsoleOutputBuffer();
        buffer.Append("text");

        buffer.Clear();

        buffer.Snapshot().Should().BeEmpty();
    }
}
