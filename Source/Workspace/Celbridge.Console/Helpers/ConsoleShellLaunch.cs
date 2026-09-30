using System.Text;
using Celbridge.Utilities;

namespace Celbridge.Console.Helpers;

/// <summary>
/// What a console's start-up needs: the folder of generated files for its mode, the project's shell history
/// folder, whether the user's profile runs, and the folder zsh would have read the user's own files from when
/// the application inherited one.
/// </summary>
public sealed record ConsoleStartupOptions(
    string ModeFolder,
    string HistoryFolder,
    bool UseShellProfile,
    string? UserZdotdir);

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
    /// The launch of a shell on its generated start-up files. A shell with none starts as it is.
    /// </summary>
    public static ConsoleShellLaunch Build(ConsoleShell shell, ConsoleStartupOptions options)
    {
        var environment = new Dictionary<string, string>();

        if (shell.IsZsh)
        {
            // A login shell, as a terminal starts, reading its start-up files from the generated folder.
            environment["ZDOTDIR"] = Path.Combine(options.ModeFolder, ConsoleStartupFiles.ZshFolder);
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
            // bash reads an rc file only when it is not a login shell, so the generated file reads the login
            // files itself.
            environment[ConsoleStartupFiles.HistoryVariable] = Path.Combine(options.HistoryFolder, BashHistoryFile);

            var bashCommandLine = new CommandLineBuilder(shell.Executable)
                .Add("--rcfile", Path.Combine(options.ModeFolder, ConsoleStartupFiles.BashRcFile))
                .Add("-i")
                .ToString();

            return new ConsoleShellLaunch(bashCommandLine, environment);
        }

        if (shell.Family == ConsoleShellFamily.PowerShell)
        {
            environment[ConsoleStartupFiles.HistoryVariable] = Path.Combine(options.HistoryFolder, PowerShellHistoryFile);

            // Encoded so no character in the start-up needs quoting for the command line.
            var startup = ConsoleStartupFiles.BuildPowerShellStartup(options.UseShellProfile);
            var encodedStartup = Convert.ToBase64String(Encoding.Unicode.GetBytes(startup));

            var powerShellCommandLine = new CommandLineBuilder(shell.Executable)
                .Add("-NoLogo", "-NoProfile", "-NoExit", "-EncodedCommand", encodedStartup)
                .ToString();

            return new ConsoleShellLaunch(powerShellCommandLine, environment);
        }

        return Bare(shell);
    }

    /// <summary>
    /// The launch of a shell with no start-up files, for when they could not be written.
    /// </summary>
    public static ConsoleShellLaunch Bare(ConsoleShell shell)
    {
        var commandLine = new CommandLineBuilder(shell.Executable).ToString();

        return new ConsoleShellLaunch(commandLine, new Dictionary<string, string>());
    }
}
