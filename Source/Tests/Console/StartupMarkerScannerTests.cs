using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class StartupMarkerScannerTests
{
    private const string Marker = "CELBRIDGE-CONSOLE-READY-a1b2c3d4";

    [Test]
    public void Push_OutputWithoutTheMarker_PassesThrough()
    {
        var scanner = new StartupMarkerScanner(Marker);

        var (text, found) = scanner.Push("PS C:\\> ");

        found.Should().BeFalse();
        text.Should().Be("PS C:\\> ");
    }

    [Test]
    public void Push_ChunkContainingTheMarker_EmitsOnlyTheRemainder()
    {
        var scanner = new StartupMarkerScanner(Marker);

        var (text, found) = scanner.Push($"\u001b[2J{Marker}banner");

        found.Should().BeTrue();
        text.Should().Be("banner");
    }

    [Test]
    public void Push_MarkerSplitAcrossChunks_IsDetectedWithoutEmittingThePartialMatch()
    {
        var scanner = new StartupMarkerScanner(Marker);
        var head = Marker.Substring(0, 10);
        var tail = Marker.Substring(10);

        var first = scanner.Push("cleared" + head);
        first.Found.Should().BeFalse();
        first.Text.Should().Be("cleared");

        var second = scanner.Push(tail + ">>> ");
        second.Found.Should().BeTrue();
        second.Text.Should().Be(">>> ");
    }

    [Test]
    public void Push_TheEchoedCommandLine_DoesNotMatchTheSplitMarker()
    {
        var scanner = new StartupMarkerScanner(Marker);
        var echoed = "Clear-Host; Write-Host -NoNewline ('CELBRIDGE-CONSOLE' + '-READY-a1b2c3d4'); celbridge-py";

        var (text, found) = scanner.Push(echoed);

        found.Should().BeFalse();
        text.Should().Be(echoed);
    }

    [Test]
    public void Flush_ReleasesHeldBackText()
    {
        var scanner = new StartupMarkerScanner(Marker);
        var head = Marker.Substring(0, 10);

        scanner.Push("noise" + head).Text.Should().Be("noise");
        scanner.Flush().Should().Be(head);
        scanner.Flush().Should().BeEmpty();
    }
}
