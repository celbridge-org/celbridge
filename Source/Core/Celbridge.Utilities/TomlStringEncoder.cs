using System.Globalization;
using System.Text;

namespace Celbridge.Utilities;

/// <summary>
/// Encodes values as TOML string literals. The TOML writers in this codebase serialize by hand so they keep
/// control over key order and layout, and this is the escaping every one of them shares.
/// </summary>
public static class TomlStringEncoder
{
    /// <summary>
    /// Encodes a value as a quoted TOML basic string. Characters with a named escape take it, so the escape
    /// reads as itself in a diff; any other control character is emitted as \uXXXX, which the form requires.
    /// </summary>
    public static string EncodeBasicString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);

        builder.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;

                case '\\':
                    builder.Append("\\\\");
                    break;

                case '\b':
                    builder.Append("\\b");
                    break;

                case '\t':
                    builder.Append("\\t");
                    break;

                case '\n':
                    builder.Append("\\n");
                    break;

                case '\f':
                    builder.Append("\\f");
                    break;

                case '\r':
                    builder.Append("\\r");
                    break;

                default:
                    AppendVerbatimOrEscaped(builder, character);
                    break;
            }
        }

        builder.Append('"');

        return builder.ToString();
    }

    /// <summary>
    /// Encodes a value as a multi-line TOML basic string, which carries newlines and tabs verbatim so the
    /// value reads as itself on disk. A run of three quotes, or a quote ending the value, is escaped so it
    /// cannot merge with the closing delimiter.
    /// </summary>
    public static string EncodeMultilineBasicString(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        // The newline straight after the opening delimiter is dropped by the parser, so starting the
        // content on its own line does not change the value.
        builder.Append("\"\"\"\n");

        var quoteRun = 0;

        foreach (var character in value)
        {
            if (character == '\\')
            {
                builder.Append("\\\\");
                quoteRun = 0;
                continue;
            }

            if (character == '"')
            {
                quoteRun++;

                if (quoteRun == 3)
                {
                    builder.Append("\\\"");
                    quoteRun = 0;
                }
                else
                {
                    builder.Append('"');
                }

                continue;
            }

            quoteRun = 0;

            switch (character)
            {
                // The characters this form exists to carry, held as themselves.
                case '\n':
                case '\r':
                case '\t':
                    builder.Append(character);
                    break;

                case '\b':
                    builder.Append("\\b");
                    break;

                case '\f':
                    builder.Append("\\f");
                    break;

                default:
                    AppendVerbatimOrEscaped(builder, character);
                    break;
            }
        }

        if (quoteRun > 0)
        {
            // The value ends in one or two quotes, which would merge with the closing delimiter. Escaping
            // the last of them breaks the run without changing what the value reads back as.
            builder.Remove(builder.Length - 1, 1);
            builder.Append("\\\"");
        }

        builder.Append("\"\"\"");

        return builder.ToString();
    }

    // A control character has no verbatim form in either kind of string, so it falls back to \uXXXX.
    private static void AppendVerbatimOrEscaped(StringBuilder builder, char character)
    {
        if (character >= ' '
            && character != '\u007f')
        {
            builder.Append(character);
            return;
        }

        builder.Append("\\u");
        builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
    }
}
