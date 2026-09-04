namespace Celbridge.Console.Helpers;

/// <summary>
/// A composed shell startup line and the exact marker bytes the host scans the output stream for. A null
/// ScanMarker means the line emits no marker, so the session reveals on its timer instead.
/// </summary>
public sealed record ComposedStartup(string Line, string? ScanMarker);

/// <summary>
/// Composes a startup command into a single line safe to inject at a shell prompt, quoting each token for
/// the target shell's dialect. Given a ready marker the line also clears the screen and emits the marker
/// before running the command, so the shell wipes its own startup noise (banner, prompt, the echoed line)
/// and the document knows the exact moment the screen is ready to reveal. A shell whose marker is invisible
/// still gets the clear-and-mark reveal with no command, so a plain shell also starts on a clean screen.
/// </summary>
public static class ShellCommandComposer
{
    // Private OSC identifier carrying the POSIX ready marker. Any number the terminal does not itself
    // handle works: the host consumes the sequence before xterm.js sees it, and a leaked marker renders as
    // nothing rather than visible text.
    private const string PosixReadyMarkerOscCode = "7000";

    /// <summary>
    /// Whether a shell family can emit a ready marker. Cmd has no concise way to write a string whose
    /// source text differs from its output, so a cmd console reveals on the document's timer instead.
    /// </summary>
    public static bool SupportsReadyMarker(ConsoleShellFamily family)
    {
        return family != ConsoleShellFamily.Cmd;
    }

    public static ComposedStartup Compose(
        ConsoleShellFamily family,
        ConsoleStartupInvocation command,
        string? readyMarker = null,
        string? workingDirectory = null)
    {
        var hasExecutable = !string.IsNullOrWhiteSpace(command.Executable);

        // A plain shell injects no command, but a shell whose marker is invisible still clears the startup
        // noise and marks the ready point so the buffer begins on a clean prompt. A visible-marker shell
        // cannot reveal here without leaving the cursor mid-line, so it reveals nothing.
        if (!hasExecutable)
        {
            if (readyMarker is not null &&
                family == ConsoleShellFamily.Posix)
            {
                var revealOnly = BuildReveal(family, readyMarker);
                return new ComposedStartup(revealOnly.Prefix.TrimEnd(), revealOnly.ScanMarker);
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

        // An injected command runs as soon as the shell is up, before its own startup has necessarily
        // finished syncing its provider location to the process directory CreateProcess was given. Setting
        // the location explicitly, right before the command, does not depend on that sync having happened.
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            line = BuildChangeDirectory(family, workingDirectory) + line;
        }

        string? scanMarker = null;
        if (readyMarker is not null)
        {
            var reveal = BuildReveal(family, readyMarker);
            line = reveal.Prefix + line;
            scanMarker = reveal.ScanMarker;
        }

        return new ComposedStartup(line, scanMarker);
    }

    // Navigates to the resolved working directory right before the command, in the shell's own syntax
    // rather than relying on it having already picked up the process directory CreateProcess was started
    // with.
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

    // The reveal injected before the command: clears the screen and emits the marker. The prefix's source
    // text never contains the scan marker, so the shell's echo of the injected line cannot match it. POSIX
    // emits an invisible escape sequence whose printf source differs from its output; PowerShell splits the
    // marker across two concatenated literals.
    private static (string Prefix, string? ScanMarker) BuildReveal(ConsoleShellFamily family, string readyMarker)
    {
        switch (family)
        {
            case ConsoleShellFamily.PowerShell:
            {
                var splitIndex = readyMarker.Length / 2;
                var head = readyMarker.Substring(0, splitIndex);
                var tail = readyMarker.Substring(splitIndex);
                var prefix = $"Clear-Host; Write-Host -NoNewline ('{head}' + '{tail}'); ";
                return (prefix, readyMarker);
            }

            case ConsoleShellFamily.Cmd:
                return ("cls & ", null);

            default:
            {
                // Invisible, cursor-neutral OSC carrying the marker: leaves the shell at column 0 so a
                // reveal with no following command does not trip zsh's partial-line indicator.
                var prefix = $"clear; printf '{PosixMarkerPrintfSource(readyMarker)}'; ";
                return (prefix, PosixMarkerStreamBytes(readyMarker));
            }
        }
    }

    // The OSC marker as printf source text: backslash-escaped ESC and BEL, so its literal form differs from
    // the bytes printf writes.
    private static string PosixMarkerPrintfSource(string readyMarker)
    {
        return $"\\033]{PosixReadyMarkerOscCode};{readyMarker}\\007";
    }

    // The OSC marker as it appears in the output stream: real ESC and BEL bytes, which is what the host
    // scans for.
    private static string PosixMarkerStreamBytes(string readyMarker)
    {
        var escape = (char)27;
        var bell = (char)7;
        return $"{escape}]{PosixReadyMarkerOscCode};{readyMarker}{bell}";
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

    // Quotes for the shell's dialect whether or not the text strictly needs it. A path always takes this
    // route rather than going through NeedsQuoting, whose safe set is drawn for command tokens and passes
    // characters a folder name can legitimately hold. A comma is the one that bites: PowerShell reads an
    // unquoted one in argument position as an array separator, which hands Set-Location a path made of
    // the parts joined by a space.
    private static string Quote(ConsoleShellFamily family, string token)
    {
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
