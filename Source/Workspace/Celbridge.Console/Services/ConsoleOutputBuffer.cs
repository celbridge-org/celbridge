using System.Text;

namespace Celbridge.Console.Services;

/// <summary>
/// A bounded scrollback buffer of raw terminal output, replayed into a view when it attaches to a running
/// session. Thread-safe: the pty read loop appends while the attach path snapshots.
/// </summary>
public sealed class ConsoleOutputBuffer
{
    // Comfortably above xterm's default 1000-line scrollback at wide terminal widths.
    private const int MaxChars = 256_000;

    private readonly object _lock = new();
    private readonly StringBuilder _buffer = new();

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_lock)
        {
            _buffer.Append(text);

            if (_buffer.Length > MaxChars)
            {
                var excess = _buffer.Length - MaxChars;

                // Cut at the next line break past the excess so a replay does not begin mid escape
                // sequence. If none exists the raw cut stands.
                var cut = excess;
                while (cut < _buffer.Length && _buffer[cut] != '\n')
                {
                    cut++;
                }
                if (cut < _buffer.Length)
                {
                    cut++;
                }

                _buffer.Remove(0, cut);
            }
        }
    }

    public string Snapshot()
    {
        lock (_lock)
        {
            return _buffer.ToString();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
        }
    }
}
