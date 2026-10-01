using Celbridge.Console.Helpers;
using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class SharedConsoleStartupWriteTests
{
    private static readonly ConsoleStartupOptions Options = new("/data/console", "/data/console/history");

    [Test]
    public async Task GetAsync_ConsolesStartingTogether_ShareOneWrite()
    {
        var writes = 0;
        var release = new TaskCompletionSource();
        var shared = new SharedConsoleStartupWrite(async () =>
        {
            Interlocked.Increment(ref writes);
            await release.Task;
            return Result<ConsoleStartupOptions>.Ok(Options);
        });

        var first = shared.GetAsync();
        var second = shared.GetAsync();
        release.SetResult();
        var later = shared.GetAsync();

        (await first).Value.Should().Be(Options);
        (await second).Value.Should().Be(Options);
        (await later).Value.Should().Be(Options);
        writes.Should().Be(1);
    }

    [Test]
    public async Task GetAsync_AfterAFailedWrite_WritesAgain()
    {
        var writes = 0;
        var shared = new SharedConsoleStartupWrite(() =>
        {
            writes++;
            if (writes == 1)
            {
                return Task.FromResult(Result<ConsoleStartupOptions>.Fail("The disk is full."));
            }

            return Task.FromResult(Result<ConsoleStartupOptions>.Ok(Options));
        });

        (await shared.GetAsync()).IsFailure.Should().BeTrue();
        (await shared.GetAsync()).IsSuccess.Should().BeTrue();
        (await shared.GetAsync()).IsSuccess.Should().BeTrue();
        writes.Should().Be(2);
    }
}
