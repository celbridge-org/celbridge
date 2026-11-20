using Celbridge.LanguageServers.Services;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the messages waiting for a page.
/// </summary>
[TestFixture]
public class PageOutboxTests
{
    [Test]
    public async Task ReadNext_ReturnsTheMessagesInTheOrderTheyWereAdded()
    {
        var outbox = new PageOutbox();
        outbox.Enqueue("first");
        outbox.Enqueue("second");

        var first = await outbox.ReadNextAsync(CancellationToken.None);
        var second = await outbox.ReadNextAsync(CancellationToken.None);

        first.Should().Be("first");
        second.Should().Be("second");
    }

    [Test]
    public async Task Complete_ReturnsTheWaitingMessagesAndThenNull()
    {
        var outbox = new PageOutbox();
        outbox.Enqueue("last");
        outbox.Complete();
        outbox.Enqueue("too late");

        var first = await outbox.ReadNextAsync(CancellationToken.None);
        var second = await outbox.ReadNextAsync(CancellationToken.None);
        var third = await outbox.ReadNextAsync(CancellationToken.None);

        first.Should().Be("last");
        second.Should().BeNull();
        third.Should().BeNull();
    }

    [Test]
    public async Task ReadNext_WaitsForAMessageAddedLater()
    {
        var outbox = new PageOutbox();

        var readTask = outbox.ReadNextAsync(CancellationToken.None).AsTask();
        readTask.IsCompleted.Should().BeFalse();
        outbox.Enqueue("late");
        var message = await readTask;

        message.Should().Be("late");
    }

    private static async Task<List<string>> ReadAllAsync(PageOutbox outbox)
    {
        var messages = new List<string>();
        while (true)
        {
            var message = await outbox.ReadNextAsync(CancellationToken.None);
            if (message is null)
            {
                break;
            }

            messages.Add(message);
        }

        return messages;
    }
}
