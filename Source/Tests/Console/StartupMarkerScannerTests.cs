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
    public void Push_MarkerRepaintedAfterTheFirstMatch_IsStrippedWithTheSurroundingOutputKept()
    {
        var scanner = new StartupMarkerScanner(Marker);
        scanner.Push($"noise{Marker}").Found.Should().BeTrue();

        // A resize reflows the rows a screen-text marker was written to, putting it back on the stream.
        var (text, found) = scanner.Push($"\u001b[H{Marker}Welcome to Node.js");

        found.Should().BeTrue();
        text.Should().Be("\u001b[HWelcome to Node.js");
    }

    [Test]
    public void Push_RepaintedMarkerSplitAcrossChunks_IsStillStripped()
    {
        var scanner = new StartupMarkerScanner(Marker);
        scanner.Push(Marker).Found.Should().BeTrue();

        var head = Marker.Substring(0, 10);
        var tail = Marker.Substring(10);

        var first = scanner.Push("banner" + head);
        first.Found.Should().BeFalse();
        first.Text.Should().Be("banner");

        var second = scanner.Push(tail + " more");
        second.Found.Should().BeTrue();
        second.Text.Should().Be(" more");
    }

    [Test]
    public void Push_SingleCharacterMarker_HoldsNothingBack()
    {
        var scanner = new StartupMarkerScanner("\u2404");

        scanner.Push("noise\u2404").Found.Should().BeTrue();

        var (text, found) = scanner.Push("C");
        found.Should().BeFalse();
        text.Should().Be("C");
    }

    [Test]
    public void Push_SeveralRepaintsInOneChunk_AreAllStripped()
    {
        var scanner = new StartupMarkerScanner(Marker);
        scanner.Push(Marker).Found.Should().BeTrue();

        var (text, found) = scanner.Push($"one{Marker}two{Marker}three");

        found.Should().BeTrue();
        text.Should().Be("onetwothree");
    }

    [Test]
    public void Flush_ReleasesHeldBackText()
    {
        var scanner = new StartupMarkerScanner(Marker);
        var head = Marker.Substring(0, 10);

        // Held back rather than emitted, so a marker split across two chunks is never printed as output.
        scanner.Push("noise" + head).Text.Should().Be("noise");
        scanner.Flush().Should().Be(head);
        scanner.Flush().Should().BeEmpty();
    }
}
