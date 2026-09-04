namespace Celbridge.Console.Services;

/// <summary>
/// Watches a terminal output stream for the session's ready marker, which separates shell-startup noise
/// from real session output. Push emits pre-marker text chunk by chunk, for the caller to discard or
/// forward. On the chunk containing the marker it emits only the post-marker remainder, and a trailing
/// partial match is held back so a marker split across two chunks is never emitted early. Pushing past the
/// first match strips any later copy of the marker and leaves the output around it in place.
/// </summary>
public sealed class StartupMarkerScanner
{
    private readonly string _marker;
    private string _buffer = string.Empty;
    private bool _found;

    public StartupMarkerScanner(string marker)
    {
        _marker = marker;
    }

    /// <summary>
    /// Releases any held-back text, for when no marker can still arrive and scanning stops.
    /// </summary>
    public string Flush()
    {
        var text = _buffer;
        _buffer = string.Empty;
        return text;
    }

    public (string Text, bool Found) Push(string text)
    {
        _buffer += text;

        var emitted = string.Empty;
        var found = false;

        var index = _buffer.IndexOf(_marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            // Text before the first match is shell-startup noise and goes no further. Text before a later
            // match is real output around a repainted marker.
            if (_found)
            {
                emitted += _buffer.Substring(0, index);
            }

            _buffer = _buffer.Substring(index + _marker.Length);
            _found = true;
            found = true;

            index = _buffer.IndexOf(_marker, StringComparison.Ordinal);
        }

        var held = PartialMatchLength();
        emitted += _buffer.Substring(0, _buffer.Length - held);
        _buffer = _buffer.Substring(_buffer.Length - held);

        return (emitted, found);
    }

    // The length of the longest suffix of the buffer that is also a prefix of the marker.
    private int PartialMatchLength()
    {
        var maximum = Math.Min(_buffer.Length, _marker.Length - 1);
        for (var length = maximum; length > 0; length--)
        {
            if (_buffer.EndsWith(_marker.Substring(0, length), StringComparison.Ordinal))
            {
                return length;
            }
        }

        return 0;
    }
}
