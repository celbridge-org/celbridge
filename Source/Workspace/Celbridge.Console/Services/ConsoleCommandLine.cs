using System.Text;

namespace Celbridge.Console.Services;

/// <summary>
/// Builds a single command-line string from an executable and its arguments, quoting each token for the
/// current platform. On Windows CreateProcess parses the string with a null application name (the first
/// token is the PATH-searched executable). On the Unix heads the string is handed to /bin/sh -c.
/// </summary>
internal static class ConsoleCommandLine
{
    public static string Build(string executable, IReadOnlyList<string> arguments)
    {
        var parts = new List<string>
        {
            Quote(executable)
        };

        foreach (var argument in arguments)
        {
            parts.Add(Quote(argument));
        }

        return string.Join(" ", parts);
    }

    private static string Quote(string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            // POSIX single-quote quoting: close, insert an escaped quote, reopen.
            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }

        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var needsQuoting = value.Any(character =>
            character == ' ' ||
            character == '\t' ||
            character == '\n' ||
            character == '\v' ||
            character == '"');

        if (!needsQuoting)
        {
            return value;
        }

        // Windows CreateProcess command-line quoting: a run of backslashes before a quote is doubled and
        // the quote escaped. A trailing run before the closing quote is doubled.
        var builder = new StringBuilder();
        builder.Append('"');

        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }

            builder.Append(character);
        }

        if (backslashes > 0)
        {
            builder.Append('\\', backslashes * 2);
        }

        builder.Append('"');

        return builder.ToString();
    }
}
