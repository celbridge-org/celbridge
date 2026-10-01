namespace Celbridge.Console.Helpers;

/// <summary>
/// The marker a console's start-up prints when the shell's own start-up has finished, just before the console's
/// command runs. The host drops everything printed before the marker, and shows the console when it arrives.
/// ScanText is the exact text the host looks for in the output.
/// PersistsOnScreen means the marker is written into a screen cell. A terminal that models the screen sends the
/// cell again when it reflows, so the marker can arrive more than once.
/// </summary>
public sealed record ConsoleReadyMarker(string ScanText, bool PersistsOnScreen)
{
    /// <summary>
    /// The marker character a PowerShell console writes. The output reader decodes UTF-8 statefully and passes on
    /// only whole characters, so a single-character marker is never split across two chunks.
    /// </summary>
    public const char PowerShellCharacter = '␄';

    /// <summary>
    /// The private OSC sequence a zsh or bash console writes, as printf source text. Any OSC number the terminal
    /// does not handle would work, because the terminal shows nothing for it.
    /// </summary>
    public const string PosixPrintfSource = @"\033]7000;CELBRIDGE-CONSOLE-READY\007";

    private const string PosixScanText = "\u001b]7000;CELBRIDGE-CONSOLE-READY\u0007";

    /// <summary>
    /// Returns the marker a console's start-up prints, or null when it prints none. zsh and bash always print an
    /// invisible OSC sequence before the first prompt. PowerShell's marker is a visible character. Without a
    /// command it would be left in front of the first prompt, so PowerShell prints it only when there is one.
    /// </summary>
    public static ConsoleReadyMarker? For(ConsoleShell shell, bool hasCommand)
    {
        switch (shell.Family)
        {
            case ConsoleShellFamily.Posix:
                return new ConsoleReadyMarker(PosixScanText, PersistsOnScreen: false);

            case ConsoleShellFamily.PowerShell when hasCommand:
                // Write-Host puts the marker in a screen cell. ConPTY sends screen cells again on every reflow, so
                // the marker can arrive again while it is on screen.
                return new ConsoleReadyMarker(PowerShellCharacter.ToString(), PersistsOnScreen: true);

            default:
                return null;
        }
    }
}
