namespace Celbridge.Console.Helpers;

/// <summary>
/// The marker a console's start-up emits once the shell's own start-up is done, just before the console's
/// command runs. The host discards everything the shell printed before it as start-up noise, and reveals the
/// console when it arrives. ScanText is the exact text the host scans the output stream for.
/// PersistsOnScreen means the marker was written into a screen cell, which a terminal that models a screen
/// puts back on the stream when it reflows those rows.
/// </summary>
public sealed record ConsoleReadyMarker(string ScanText, bool PersistsOnScreen)
{
    /// <summary>
    /// The character a PowerShell console writes. The reader decodes UTF-8 with a stateful decoder and hands on
    /// whole characters, so a single-character marker never arrives split across two chunks.
    /// </summary>
    public const char PowerShellCharacter = '␄';

    /// <summary>
    /// The private OSC a zsh or bash console writes, as printf source text. Any number the terminal does not
    /// itself handle works, since such a sequence renders as nothing if it reaches one.
    /// </summary>
    public const string PosixPrintfSource = @"\033]7000;CELBRIDGE-CONSOLE-READY\007";

    private const string PosixScanText = "\u001b]7000;CELBRIDGE-CONSOLE-READY\u0007";

    /// <summary>
    /// The marker a console's start-up emits, or null when it emits none. zsh and bash emit an invisible,
    /// cursor-neutral OSC before every console's first prompt. PowerShell's marker is a visible character,
    /// which a console with no command would leave in front of its first prompt, so only a console with a
    /// command emits it.
    /// </summary>
    public static ConsoleReadyMarker? For(ConsoleShell shell, bool hasCommand)
    {
        switch (shell.Family)
        {
            case ConsoleShellFamily.Posix:
                return new ConsoleReadyMarker(PosixScanText, PersistsOnScreen: false);

            case ConsoleShellFamily.PowerShell when hasCommand:
                // Write-Host puts the marker in a screen cell, and ConPTY reserialises those cells on every
                // reflow, so the marker comes back on the stream for as long as it is on screen.
                return new ConsoleReadyMarker(PowerShellCharacter.ToString(), PersistsOnScreen: true);

            default:
                return null;
        }
    }
}
