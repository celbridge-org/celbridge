using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class DiagnosticSequenceScannerTests
{
    private const string Escape = "";
    private const string Bell = "";

    private static string Diagnostic(string payload)
    {
        return $"{Escape}]7001;{payload}{Bell}";
    }

    [Test]
    public void OrdinaryOutput_PassesThroughUnchanged()
    {
        var scanner = new DiagnosticSequenceScanner();

        var scanned = scanner.Push("hello world\n");

        scanned.Text.Should().Be("hello world\n");
        scanned.Diagnostics.Should().BeEmpty();
    }

    [Test]
    public void UnrelatedEscapeSequences_PassThroughUnchanged()
    {
        // The stream is full of other sequences, including the ready marker on the neighbouring code.
        var scanner = new DiagnosticSequenceScanner();
        var output = $"{Escape}[2J{Escape}]0;a title{Bell}{Escape}]7000;READY-abc{Bell}text";

        var scanned = scanner.Push(output);

        scanned.Text.Should().Be(output);
        scanned.Diagnostics.Should().BeEmpty();
    }

    [Test]
    public void Diagnostic_IsLiftedOutAndRemovedFromTheOutput()
    {
        var scanner = new DiagnosticSequenceScanner();

        var scanned = scanner.Push($"before{Diagnostic("python-probe mode=offline ms=412")}after");

        scanned.Text.Should().Be("beforeafter");
        scanned.Diagnostics.Should().Equal("python-probe mode=offline ms=412");
    }

    [Test]
    public void SequenceSplitAcrossChunks_IsNeverEmittedAsTerminalOutput()
    {
        var scanner = new DiagnosticSequenceScanner();
        var sequence = Diagnostic("python-probe ms=1");

        var first = scanner.Push("out" + sequence.Substring(0, 4));
        var second = scanner.Push(sequence.Substring(4) + "rest");

        first.Text.Should().Be("out");
        first.Diagnostics.Should().BeEmpty();
        second.Text.Should().Be("rest");
        second.Diagnostics.Should().Equal("python-probe ms=1");
    }

    [Test]
    public void MultipleDiagnostics_InOneChunkAreAllLifted()
    {
        var scanner = new DiagnosticSequenceScanner();

        var scanned = scanner.Push($"{Diagnostic("one")}middle{Diagnostic("two")}");

        scanned.Text.Should().Be("middle");
        scanned.Diagnostics.Should().Equal("one", "two");
    }

    [Test]
    public void PartialIntroducerAtTheEndOfTheStream_IsReleasedOnFlush()
    {
        var scanner = new DiagnosticSequenceScanner();

        var scanned = scanner.Push($"tail{Escape}]70");

        scanned.Text.Should().Be("tail");
        scanner.Flush().Should().Be($"{Escape}]70");
    }

    [Test]
    public void UnterminatedSequenceThatRunsTooLong_IsReleasedAsOrdinaryOutput()
    {
        var scanner = new DiagnosticSequenceScanner();
        var runaway = $"{Escape}]7001;" + new string('x', 600);

        var scanned = scanner.Push(runaway);

        scanned.Text.Should().Be(runaway);
        scanned.Diagnostics.Should().BeEmpty();
    }

    [Test]
    public void ChunkWithNoEscapeByte_IsReturnedWithoutCopying()
    {
        // The common case on a session's output path: the scan must not rewrite the chunk.
        var scanner = new DiagnosticSequenceScanner();
        var chunk = "a busy line of build output";

        var scanned = scanner.Push(chunk);

        scanned.Text.Should().BeSameAs(chunk);
    }
}
