using System.Text;
using System.Text.Json.Nodes;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// Reads and writes Language Server Protocol messages on a server's stdio streams. Each message is a JSON object
/// behind a Content-Length header. One reader and one writer may use the stream at the same time.
/// </summary>
internal sealed class LspMessageStream
{
    private const string ContentLengthHeader = "Content-Length:";

    // A header line longer than this marks a stream that is not speaking the protocol.
    private const int MaximumHeaderLineLength = 1024;

    private readonly Stream _serverInput;
    private readonly Stream _serverOutput;
    private readonly byte[] _readBuffer = new byte[16384];
    private int _readBufferStart;
    private int _readBufferEnd;

    public LspMessageStream(Stream serverInput, Stream serverOutput)
    {
        _serverInput = serverInput;
        _serverOutput = serverOutput;
    }

    /// <summary>
    /// Writes one message to the server, given as JSON text.
    /// </summary>
    public async Task WriteAsync(string messageJson, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(messageJson);
        var headerBytes = Encoding.ASCII.GetBytes($"{ContentLengthHeader} {bodyBytes.Length}\r\n\r\n");

        await _serverInput.WriteAsync(headerBytes, cancellationToken);
        await _serverInput.WriteAsync(bodyBytes, cancellationToken);
        await _serverInput.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the next message from the server. Returns null when the server closes its output.
    /// </summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken cancellationToken)
    {
        var contentLength = -1;
        while (true)
        {
            var headerLine = await ReadHeaderLineAsync(cancellationToken);
            if (headerLine is null)
            {
                return null;
            }

            // An empty line ends the headers.
            if (headerLine.Length == 0)
            {
                break;
            }

            if (headerLine.StartsWith(ContentLengthHeader, StringComparison.OrdinalIgnoreCase))
            {
                var lengthText = headerLine.Substring(ContentLengthHeader.Length).Trim();
                if (!int.TryParse(lengthText, out contentLength))
                {
                    throw new FormatException($"The server sent the header '{headerLine}'");
                }
            }
        }

        if (contentLength < 0)
        {
            throw new FormatException("The server sent a message with no Content-Length header");
        }

        var bodyBytes = new byte[contentLength];
        var bodyLength = 0;
        while (bodyLength < contentLength)
        {
            if (_readBufferStart == _readBufferEnd
                && !await FillReadBufferAsync(cancellationToken))
            {
                return null;
            }

            var copyCount = Math.Min(contentLength - bodyLength, _readBufferEnd - _readBufferStart);
            Array.Copy(_readBuffer, _readBufferStart, bodyBytes, bodyLength, copyCount);
            _readBufferStart += copyCount;
            bodyLength += copyCount;
        }

        var bodyText = Encoding.UTF8.GetString(bodyBytes);
        var message = JsonNode.Parse(bodyText) as JsonObject;
        if (message is null)
        {
            throw new FormatException("The server sent a message that is not a JSON object");
        }

        return message;
    }

    // Returns the next header line with the line ending removed, or null at the end of the stream.
    private async Task<string?> ReadHeaderLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (_readBufferStart == _readBufferEnd
                && !await FillReadBufferAsync(cancellationToken))
            {
                return null;
            }

            var character = (char)_readBuffer[_readBufferStart];
            _readBufferStart++;

            if (character == '\n')
            {
                return line.ToString().TrimEnd('\r');
            }

            line.Append(character);
            if (line.Length > MaximumHeaderLineLength)
            {
                throw new FormatException("The server sent a header line that is too long");
            }
        }
    }

    private async Task<bool> FillReadBufferAsync(CancellationToken cancellationToken)
    {
        _readBufferStart = 0;
        _readBufferEnd = await _serverOutput.ReadAsync(_readBuffer, cancellationToken);

        return _readBufferEnd > 0;
    }
}
