using System.Text;

namespace Celbridge.Console.Services;

/// <summary>
/// The result of scanning a chunk of terminal output: the text to pass on, and any diagnostics lifted
/// out of it.
/// </summary>
public sealed record ScannedOutput(string Text, IReadOnlyList<string> Diagnostics);

/// <summary>
/// Strips host diagnostics from a terminal output stream, so a launched command can report to the
/// application log before its client connects, without the message reaching the user's terminal. A
/// diagnostic is a private OSC sequence, which a terminal that never sees it would render as nothing.
/// A trailing partial match is held back, so a sequence split across two chunks is never emitted as
/// terminal output.
/// </summary>
public sealed class DiagnosticSequenceScanner
{
    // Private OSC identifier carrying a host diagnostic, alongside the ready marker's 7000.
    private const string DiagnosticIntroducer = "]7001;";
    private const char SequenceTerminator = '';

    // A started sequence that runs this long without its terminator is not a diagnostic, so it is
    // released rather than held back indefinitely.
    private const int MaximumHeldLength = 512;

    private string _buffer = string.Empty;

    public ScannedOutput Push(string text)
    {
        // The overwhelmingly common chunk carries no escape byte at all. Returning it untouched keeps
        // the scan on the session's output path free of allocation.
        if (_buffer.Length == 0
            && text.IndexOf(DiagnosticIntroducer[0]) < 0)
        {
            return new ScannedOutput(text, Array.Empty<string>());
        }

        _buffer += text;

        var emitted = new StringBuilder();
        var diagnostics = new List<string>();

        while (true)
        {
            var index = _buffer.IndexOf(DiagnosticIntroducer, StringComparison.Ordinal);
            if (index < 0)
            {
                var held = PartialIntroducerLength();
                emitted.Append(_buffer, 0, _buffer.Length - held);
                _buffer = _buffer.Substring(_buffer.Length - held);
                break;
            }

            emitted.Append(_buffer, 0, index);
            var remainder = _buffer.Substring(index);

            var terminatorIndex = remainder.IndexOf(SequenceTerminator);
            if (terminatorIndex < 0)
            {
                if (remainder.Length > MaximumHeldLength)
                {
                    emitted.Append(remainder);
                    _buffer = string.Empty;
                    break;
                }

                _buffer = remainder;
                break;
            }

            var payloadLength = terminatorIndex - DiagnosticIntroducer.Length;
            diagnostics.Add(remainder.Substring(DiagnosticIntroducer.Length, payloadLength));
            _buffer = remainder.Substring(terminatorIndex + 1);
        }

        return new ScannedOutput(emitted.ToString(), diagnostics);
    }

    /// <summary>
    /// Releases any held-back text, for when the session ends mid-sequence.
    /// </summary>
    public string Flush()
    {
        var text = _buffer;
        _buffer = string.Empty;

        return text;
    }

    // The length of the longest suffix of the buffer that is also a prefix of the introducer.
    private int PartialIntroducerLength()
    {
        var maximum = Math.Min(_buffer.Length, DiagnosticIntroducer.Length - 1);
        for (var length = maximum; length > 0; length--)
        {
            if (_buffer.EndsWith(DiagnosticIntroducer.Substring(0, length), StringComparison.Ordinal))
            {
                return length;
            }
        }

        return 0;
    }
}
