using System.Text.Json;

namespace Celbridge.Utilities;

/// <summary>
/// Composes the payload carried by IOpenDocumentCommand.Location, which names a position within a
/// document for the target editor to navigate to. The encoding is internal to the host, so callers
/// pass line and column numbers rather than building the payload themselves.
/// </summary>
public static class DocumentLocation
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Composes a location payload for a single position. Returns an empty string when no line is
    /// given, which opens the document without navigating.
    /// </summary>
    public static string Compose(int line, int column)
    {
        return Compose(line, column, endLine: 0, endColumn: 0);
    }

    /// <summary>
    /// Composes a location payload for a range, swapping the endpoints if they arrive reversed.
    /// An end line of zero means the position has no range and only the start is used.
    /// </summary>
    public static string Compose(int line, int column, int endLine, int endColumn)
    {
        if (line <= 0)
        {
            return string.Empty;
        }

        if (endLine > 0)
        {
            var endIsBeforeStart = endLine < line ||
                (endLine == line && endColumn < column);

            if (endIsBeforeStart)
            {
                (line, endLine) = (endLine, line);
                (column, endColumn) = (endColumn, column);
            }
        }

        var payload = new DocumentLocationPayload(line, column, endLine, endColumn);

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    // Serialized camelCase, which is the shape CustomEditorController reads back before forwarding the
    // position to the editor.
    private record DocumentLocationPayload(int LineNumber, int Column, int EndLineNumber, int EndColumn);
}
