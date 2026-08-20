using Celbridge.Tests.Helpers;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the sink that writes page-reported diagnostics into the application log. The text is
/// written by whatever package authored the page, so the sink has to survive anything it is handed.
/// </summary>
[TestFixture]
public class WebSurfaceLogTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan interval) => _now += interval;
    }

    private RecordingLogger<WebSurfaceLog> _logger = null!;
    private FakeTimeProvider _timeProvider = null!;
    private WebSurfaceLog _webSurfaceLog = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new RecordingLogger<WebSurfaceLog>();
        _timeProvider = new FakeTimeProvider();
        _webSurfaceLog = new WebSurfaceLog(_logger, _timeProvider);
    }

    // The sink logs the page's text as an argument rather than part of the template, so what a page said is
    // the second argument of the entry it produced.
    private static string ReportedText(LogEntry entry)
    {
        return (string)entry.Arguments[1]!;
    }

    [Test]
    public void Write_MapsTheLevelThePageAskedFor()
    {
        _webSurfaceLog.Write("editor", "error", "import failed");
        _webSurfaceLog.Write("editor", "warn", "slow frame");
        _webSurfaceLog.Write("editor", "info", "ready");

        ReportedText(_logger.EntriesAt(LogEntryLevel.Error).Single()).Should().Be("import failed");
        ReportedText(_logger.EntriesAt(LogEntryLevel.Warning).Single()).Should().Be("slow frame");
        ReportedText(_logger.EntriesAt(LogEntryLevel.Information).Single()).Should().Be("ready");
    }

    [Test]
    public void Write_WithAnUnknownLevel_LogsAsDebug()
    {
        _webSurfaceLog.Write("editor", "banana", "something happened");
        _webSurfaceLog.Write("editor", null, "something else happened");

        _logger.EntriesAt(LogEntryLevel.Debug).Should().HaveCount(2);
    }

    [Test]
    public void Write_WithNoMessage_LogsNothing()
    {
        _webSurfaceLog.Write("editor", "error", null);
        _webSurfaceLog.Write("editor", "error", "   ");

        _logger.Entries.Should().BeEmpty();
    }

    [Test]
    public void Write_NamesTheSurfaceThatReported()
    {
        _webSurfaceLog.Write("notes.note", "error", "import failed");

        var entry = _logger.EntriesAt(LogEntryLevel.Error).Single();
        entry.Arguments[0].Should().Be("notes.note");
    }

    [Test]
    public void Write_PastTheRateLimit_StopsLoggingUntilTheWindowRolls()
    {
        for (var i = 0; i < 200; i++)
        {
            _webSurfaceLog.Write("noisy", "info", $"message {i}");
        }

        // The entries up to the limit, and no more however long the page keeps going.
        _logger.EntriesAt(LogEntryLevel.Information).Should().HaveCount(49);
        _logger.EntriesAt(LogEntryLevel.Warning).Should().HaveCount(1);

        _timeProvider.Advance(TimeSpan.FromSeconds(11));
        _webSurfaceLog.Write("noisy", "info", "after the window");

        _logger.EntriesAt(LogEntryLevel.Information).Should().HaveCount(50);
    }

    [Test]
    public void Write_RateLimitsEachSurfaceSeparately()
    {
        for (var i = 0; i < 200; i++)
        {
            _webSurfaceLog.Write("noisy", "info", $"message {i}");
        }

        _webSurfaceLog.Write("quiet", "info", "still heard");

        var informationEntries = _logger.EntriesAt(LogEntryLevel.Information);
        informationEntries.Should().HaveCount(50);
        ReportedText(informationEntries[^1]).Should().Be("still heard");
    }

    [Test]
    public void Write_WithAnEnormousMessage_TruncatesIt()
    {
        var message = new string('x', 5000);

        _webSurfaceLog.Write("editor", "error", message);

        var entry = _logger.EntriesAt(LogEntryLevel.Error).Single();
        ReportedText(entry).Length.Should().BeLessThan(2100);
    }
}
