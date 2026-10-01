using System.Text;
using Celbridge.Utilities;

namespace Celbridge.Console.Helpers;

/// <summary>
/// The inputs to a console's start-up: the folder of generated files, the project's shell history folder, and
/// the console's settings. Each setting defaults to the same value as in a console document.
/// </summary>
public sealed record ConsoleStartupOptions(
    string StartupFolder,
    string HistoryFolder)
{
    /// <summary>
    /// Whether the user's own start-up files run.
    /// </summary>
    public bool UseShellProfile { get; init; } = true;

    /// <summary>
    /// Whether the start-up replaces the shell's prompt with a compact one before every prompt.
    /// </summary>
    public bool CompactPrompt { get; init; } = true;

    /// <summary>
    /// The user's own ZDOTDIR folder, if Celbridge inherited one.
    /// </summary>
    public string? UserZdotdir { get; init; }

    /// <summary>
    /// The console's command: the executable, then its arguments. The start-up runs it once the console is
    /// visible. It is empty when the console just shows the shell's prompt. No part may contain a line break,
    /// because the start-up receives each part on a line of its own.
    /// </summary>
    public IReadOnlyList<string> Command { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The folder the console's command runs in.
    /// </summary>
    public string? WorkingFolder { get; init; }
}

/// <summary>
/// How a console's shell is started: its command line, and the variables that point it at its start-up
/// files.
/// </summary>
public sealed record ConsoleShellLaunch(string CommandLine, IReadOnlyDictionary<string, string> Environment)
{
    private const string ZshHistoryFile = "zsh_history";
    private const string BashHistoryFile = "bash_history";
    private const string PowerShellHistoryFile = "powershell_history.txt";

    /// <summary>
    /// Builds the launch for a shell that uses the generated start-up files. A shell with no start-up files
    /// starts plainly.
    /// </summary>
    public static ConsoleShellLaunch Build(ConsoleShell shell, ConsoleStartupOptions options)
    {
        var environment = new Dictionary<string, string>();
        if (options.UseShellProfile)
        {
            environment[ConsoleStartupFiles.UseShellProfileVariable] = "1";
        }
        if (options.CompactPrompt)
        {
            environment[ConsoleStartupFiles.CompactPromptVariable] = "1";
        }
        if (options.Command.Count > 0)
        {
            environment[ConsoleStartupFiles.CommandVariable] = string.Join('\n', options.Command);
            if (!string.IsNullOrEmpty(options.WorkingFolder))
            {
                environment[ConsoleStartupFiles.WorkingFolderVariable] = options.WorkingFolder;
            }
        }

        if (shell.IsZsh)
        {
            // Start a login shell, as a terminal does. ZDOTDIR points it at the generated start-up files.
            environment["ZDOTDIR"] = Path.Combine(options.StartupFolder, ConsoleStartupFiles.ZshFolder);
            environment[ConsoleStartupFiles.HistoryVariable] = Path.Combine(options.HistoryFolder, ZshHistoryFile);
            if (options.UseShellProfile &&
                !string.IsNullOrEmpty(options.UserZdotdir))
            {
                environment[ConsoleStartupFiles.UserZdotdirVariable] = options.UserZdotdir;
            }

            var zshCommandLine = new CommandLineBuilder(shell.Executable)
                .Add("-l")
                .ToString();

            return new ConsoleShellLaunch(zshCommandLine, environment);
        }

        if (shell.IsBash)
        {
            // bash reads an rc file only when it is not a login shell. The generated rc file runs the login files
            // itself.
            environment[ConsoleStartupFiles.HistoryVariable] = Path.Combine(options.HistoryFolder, BashHistoryFile);

            var bashCommandLine = new CommandLineBuilder(shell.Executable)
                .Add("--rcfile", Path.Combine(options.StartupFolder, ConsoleStartupFiles.BashRcFile))
                .Add("-i")
                .ToString();

            return new ConsoleShellLaunch(bashCommandLine, environment);
        }

        if (shell.Family == ConsoleShellFamily.PowerShell)
        {
            environment[ConsoleStartupFiles.HistoryVariable] = Path.Combine(options.HistoryFolder, PowerShellHistoryFile);

            // Encode the script, so none of its characters need quoting on the command line.
            var startup = ConsoleStartupFiles.BuildPowerShellStartup();
            var encodedStartup = Convert.ToBase64String(Encoding.Unicode.GetBytes(startup));

            var powerShellCommandLine = new CommandLineBuilder(shell.Executable)
                .Add("-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand", encodedStartup)
                .ToString();

            return new ConsoleShellLaunch(powerShellCommandLine, environment);
        }

        var bareCommandLine = new CommandLineBuilder(shell.Executable).ToString();
        return new ConsoleShellLaunch(bareCommandLine, new Dictionary<string, string>());
    }
}
