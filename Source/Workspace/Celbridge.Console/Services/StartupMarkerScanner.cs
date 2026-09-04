namespace Celbridge.Console.Services;

/// <summary>
/// Watches a terminal output stream for the session's ready marker, which separates shell-startup noise
/// from real session output. Push emits pre-marker text chunk by chunk, for the caller to discard or
/// forward; on the chunk containing the marker it emits only the post-marker remainder. A trailing partial
/// match is held back so a marker split across two chunks is never emitted early. Scanning continues past
/// that first match, because a marker written as screen text sits in the terminal's own buffer and is
/// reproduced by any reflow of the rows it occupies. A later match is taken out of the stream on its own,
/// leaving the real output around it in place.
/// </summary>
public sealed class StartupMarkerScanner
{
    // The shortest trailing partial match kept back once the marker has been seen. Output ending in the
    // marker's opening character is ordinary, and a keystroke echoed at a prompt is exactly that, so
    // holding it would keep real output off the screen until something else arrived. A run this long
    // matching the marker's opening is a split marker rather than a coincidence. A marker split inside
    // that opening run is printed rather than taken out, which costs a few stray characters.
    private const int MinimumHeldPartialMatch = 4;

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
            // Text before the first match is the shell-startup noise the marker exists to separate out, so
            // it goes no further. A later match is a repaint of the cells the marker was written to, where
            // the text around it is real output and only the marker itself is taken out.
            if (_found)
            {
                emitted += _buffer.Substring(0, index);
            }

            _buffer = _buffer.Substring(index + _marker.Length);
            _found = true;
            found = true;

            index = _buffer.IndexOf(_marker, StringComparison.Ordinal);
        }

        var held = HeldPartialMatchLength();
        emitted += _buffer.Substring(0, _buffer.Length - held);
        _buffer = _buffer.Substring(_buffer.Length - held);

        return (emitted, found);
    }

    // How much of a trailing partial match to keep back for the next chunk. Until the marker has been seen
    // every partial match is held: what is emitted is startup noise the caller discards either way, so the
    // only thing at stake is detecting a marker split across two chunks. Afterwards the emitted text is the
    // session's own output, and a chunk that ends a burst may be the last for as long as the session sits
    // at a prompt, so a match too short to be meaningful is emitted instead.
    private int HeldPartialMatchLength()
    {
        var length = PartialMatchLength();

        if (_found &&
            length < MinimumHeldPartialMatch)
        {
            return 0;
        }

        return length;
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
