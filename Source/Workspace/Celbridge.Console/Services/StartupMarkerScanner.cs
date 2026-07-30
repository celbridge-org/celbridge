namespace Celbridge.Console.Services;

/// <summary>
/// Watches a terminal output stream for the session's ready marker, which separates shell-startup noise
/// from real session output. Push emits pre-marker text chunk by chunk (the caller discards it); on the
/// chunk containing the marker it emits only the post-marker remainder. A trailing partial match is held
/// back so a marker split across two chunks is never emitted early.
/// </summary>
public sealed class StartupMarkerScanner
{
    private readonly string _marker;
    private string _buffer = string.Empty;

    public StartupMarkerScanner(string marker)
    {
        _marker = marker;
    }

    public (string Text, bool Found) Push(string text)
    {
        _buffer += text;

        var index = _buffer.IndexOf(_marker, StringComparison.Ordinal);
        if (index >= 0)
        {
            var after = _buffer.Substring(index + _marker.Length);
            _buffer = string.Empty;
            return (after, true);
        }

        var held = PartialMatchLength();
        var emitted = _buffer.Substring(0, _buffer.Length - held);
        _buffer = _buffer.Substring(_buffer.Length - held);
        return (emitted, false);
    }

    /// <summary>
    /// Releases any held-back text, for when the marker never arrives and scanning gives up.
    /// </summary>
    public string Flush()
    {
        var text = _buffer;
        _buffer = string.Empty;
        return text;
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
