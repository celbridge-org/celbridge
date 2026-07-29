using System.Text;

namespace Celbridge.Console.Helpers;

/// <summary>
/// Composes a startup command into a single line safe to inject at a shell prompt, quoting each token for
/// the target shell's dialect. Given a ready marker the line also clears the screen and echoes the marker
/// before running the command, so the shell wipes its own startup noise (banner, prompt, the echoed line)
/// and the document knows the exact moment the screen is ready to reveal.
/// </summary>
public static class ShellCommandComposer
{
    /// <summary>
    /// Whether a shell family can emit a ready marker. Cmd has no concise way to write a string whose
    /// source text differs from its output, so a cmd console reveals on the document's timer instead.
    /// </summary>
    public static bool SupportsReadyMarker(ConsoleShellFamily family)
    {
        return family != ConsoleShellFamily.Cmd;
    }

    public static string Compose(ConsoleShellFamily family, ConsoleStartupInvocation command, string? readyMarker = null)
    {
        if (string.IsNullOrWhiteSpace(command.Executable))
        {
            return string.Empty;
        }

        var tokens = new List<string>
        {
            command.Executable
        };
        foreach (var argument in command.Arguments)
        {
            tokens.Add(argument);
        }

        var quotedTokens = new List<string>();
        foreach (var token in tokens)
        {
            quotedTokens.Add(QuoteToken(family, token));
        }

        var line = string.Join(" ", quotedTokens);

        // PowerShell parses a line starting with a quoted string as an expression, not an invocation, so
        // a quoted executable needs the call operator.
        if (family == ConsoleShellFamily.PowerShell &&
            quotedTokens[0] != tokens[0])
        {
            line = "& " + line;
        }

        if (readyMarker is not null)
        {
            line = StartupPrefix(family, readyMarker) + line;
        }

        return line;
    }

    // Clears the screen, then writes the ready marker. The marker is split across two adjacent string
    // literals, which the shell concatenates on execution: the line the shell echoes as it reads the
    // input therefore never contains the marker, so only the executed write matches it.
    private static string StartupPrefix(ConsoleShellFamily family, string readyMarker)
    {
        var splitIndex = readyMarker.Length / 2;
        var head = readyMarker.Substring(0, splitIndex);
        var tail = readyMarker.Substring(splitIndex);

        switch (family)
        {
            case ConsoleShellFamily.PowerShell:
                return $"Clear-Host; Write-Host -NoNewline ('{head}' + '{tail}'); ";

            case ConsoleShellFamily.Cmd:
                return "cls & ";

            default:
                return $"clear; printf '%s' '{head}''{tail}'; ";
        }
    }

    private static string QuoteToken(ConsoleShellFamily family, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return family == ConsoleShellFamily.Posix ? "''" : "\"\"";
        }

        if (!NeedsQuoting(token))
        {
            return token;
        }

        switch (family)
        {
            case ConsoleShellFamily.PowerShell:
                // Single quotes are literal in PowerShell; an embedded quote doubles.
                return "'" + token.Replace("'", "''") + "'";

            case ConsoleShellFamily.Posix:
                // POSIX single-quote quoting: close, insert an escaped quote, reopen.
                return "'" + token.Replace("'", "'\\''") + "'";

            default:
                // cmd has no literal quoting; double quotes cover spaces and redirection characters.
                return "\"" + token + "\"";
        }
    }

    // Conservative safe set: anything outside it is quoted. Alphanumerics plus the characters common in
    // paths, option flags, and package specifiers that no shell treats specially.
    private static bool NeedsQuoting(string token)
    {
        foreach (var character in token)
        {
            var isSafe = char.IsAsciiLetterOrDigit(character) ||
                character == '_' ||
                character == '-' ||
                character == '.' ||
                character == '/' ||
                character == '\\' ||
                character == ':' ||
                character == '=' ||
                character == '+' ||
                character == ',';

            if (!isSafe)
            {
                return true;
            }
        }

        return false;
    }
}
