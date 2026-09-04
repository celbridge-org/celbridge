namespace Celbridge.Console.Helpers;

/// <summary>
/// A composed shell startup line and the exact marker bytes the host scans the output stream for. A null
/// ScanMarker means the line emits no marker. MarkerPersistsOnScreen means the marker was written into a
/// screen cell, which a terminal that models a screen puts back on the stream when it reflows those rows.
/// </summary>
public sealed record ComposedStartup(string Line, string? ScanMarker, bool MarkerPersistsOnScreen = false);

/// <summary>
/// Composes a startup command into a single line safe to inject at a shell prompt, quoting each token for
/// the target shell's dialect. The line clears the screen and emits the shell family's ready marker before
/// running the command, wiping the shell's own startup noise and marking the point at which the screen is
/// ready to be revealed.
/// </summary>
public static class ShellCommandComposer
{
    // The marker a PowerShell console emits. The reader decodes UTF-8 with a stateful decoder and hands on
    // whole characters, so a single-character marker never arrives split across two chunks. The composed
    // line writes it from its code point, so the shell's echo of that line cannot match the scan.
    private const char PowerShellReadyMarker = '\u2404';

    // Private OSC identifier carrying the POSIX ready marker, and the text it carries. Any number the
    // terminal does not itself handle works, as such a sequence renders as nothing if it reaches one.
    private const string PosixReadyMarkerOscCode = "7000";
    private const string PosixReadyMarkerText = "CELBRIDGE-CONSOLE-READY";

    public static ComposedStartup Compose(
        ConsoleShellFamily family,
        ConsoleStartupInvocation command,
        string? workingDirectory = null)
    {
        var hasExecutable = !string.IsNullOrWhiteSpace(command.Executable);
        var reveal = BuildReveal(family);

        // A plain shell injects no command, but a shell whose marker is invisible still clears the startup
        // noise and marks the ready point so the buffer begins on a clean prompt. A visible-marker shell
        // cannot reveal here without leaving the cursor mid-line, so it reveals nothing.
        if (!hasExecutable)
        {
            if (family == ConsoleShellFamily.Posix)
            {
                return new ComposedStartup(reveal.Prefix.TrimEnd(), reveal.ScanMarker, reveal.PersistsOnScreen);
            }

            return new ComposedStartup(string.Empty, null);
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

        // An injected command can run before the shell has synced its own location to the process directory
        // CreateProcess was given.
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            line = BuildChangeDirectory(family, workingDirectory) + line;
        }

        line = reveal.Prefix + line;

        return new ComposedStartup(line, reveal.ScanMarker, reveal.PersistsOnScreen);
    }

    private static string BuildChangeDirectory(ConsoleShellFamily family, string workingDirectory)
    {
        var quotedPath = Quote(family, workingDirectory);

        return family switch
        {
            ConsoleShellFamily.PowerShell => $"Set-Location -LiteralPath {quotedPath}; ",
            ConsoleShellFamily.Cmd => $"cd /d {quotedPath} & ",
            _ => $"cd {quotedPath}; "
        };
    }

    // The reveal injected before the command: clears the screen and emits the marker.
    private static (string Prefix, string? ScanMarker, bool PersistsOnScreen) BuildReveal(ConsoleShellFamily family)
    {
        switch (family)
        {
            case ConsoleShellFamily.PowerShell:
            {
                // Write-Host puts the marker in a screen cell, and ConPTY reserialises those cells on every
                // reflow, so the marker comes back on the stream for as long as it is on screen.
                var prefix = $"Clear-Host; Write-Host -NoNewline ([char]0x{(int)PowerShellReadyMarker:x4}); ";
                return (prefix, PowerShellReadyMarker.ToString(), true);
            }

            case ConsoleShellFamily.Cmd:
                // Cmd cannot concisely write a string whose source text differs from its output, so it
                // emits no marker.
                return ("cls & ", null, false);

            default:
            {
                // Invisible, cursor-neutral OSC carrying the marker. It leaves the shell at column 0, so a
                // reveal with no following command does not trip zsh's partial-line indicator.
                var prefix = $"clear; printf '{PosixMarkerPrintfSource()}'; ";
                return (prefix, PosixMarkerStreamBytes(), false);
            }
        }
    }

    // The OSC marker as printf source text: backslash-escaped ESC and BEL, so its literal form differs from
    // the bytes printf writes.
    private static string PosixMarkerPrintfSource()
    {
        return $"\\033]{PosixReadyMarkerOscCode};{PosixReadyMarkerText}\\007";
    }

    // The OSC marker as it appears in the output stream: real ESC and BEL bytes, which is what the host
    // scans for.
    private static string PosixMarkerStreamBytes()
    {
        var escape = (char)27;
        var bell = (char)7;
        return $"{escape}]{PosixReadyMarkerOscCode};{PosixReadyMarkerText}{bell}";
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

        return Quote(family, token);
    }

    // Quotes for the shell's dialect whether or not the text strictly needs it. Paths always take this
    // route: PowerShell reads an unquoted comma in argument position as an array separator.
    private static string Quote(ConsoleShellFamily family, string token)
    {
        switch (family)
        {
            case ConsoleShellFamily.PowerShell:
                // Single quotes are literal in PowerShell. An embedded quote doubles.
                return "'" + token.Replace("'", "''") + "'";

            case ConsoleShellFamily.Posix:
                // POSIX single-quote quoting: close, insert an escaped quote, reopen.
                return "'" + token.Replace("'", "'\\''") + "'";

            default:
                // cmd has no literal quoting. Double quotes cover spaces and redirection characters.
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
