using Celbridge.Utilities;

namespace Celbridge.Console.Services;

/// <summary>
/// The built-in "shell" session type: a plain pty with no host RPC. A blank executable launches the
/// platform default shell. A set executable runs any process with its arguments, working folder, and
/// environment straight from the config.
/// </summary>
public sealed class ShellSessionProvider : IConsoleSessionProvider
{
    public string TypeId => "shell";

    public IReadOnlyList<ConsoleRunner> DefaultRunners => Array.Empty<ConsoleRunner>();

    public async Task<Result<ConsoleLaunchSpec>> BuildLaunchSpecAsync(ConsoleSessionContext context)
    {
        await Task.CompletedTask;

        string executable;
        if (string.IsNullOrWhiteSpace(context.Executable))
        {
            executable = ResolveDefaultShell();
        }
        else
        {
            executable = context.Executable;
        }

        var commandLineBuilder = new CommandLineBuilder(executable);
        foreach (var argument in context.Arguments)
        {
            commandLineBuilder.Add(argument);
        }
        var commandLine = commandLineBuilder.ToString();

        var workingDirectory = ConsoleWorkingFolder.Resolve(context.WorkingDirectory, context.ProjectFolderPath);

        var launchSpec = new ConsoleLaunchSpec(commandLine, workingDirectory, context.Environment);
        return launchSpec;
    }

    // The platform default shell used when the config leaves the executable blank. Each is resolvable
    // without a PATH probe: powershell.exe ships in System32, and the Unix path honours the user's $SHELL.
    private static string ResolveDefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return "powershell.exe";
        }

        var loginShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(loginShell))
        {
            return loginShell;
        }

        if (OperatingSystem.IsMacOS())
        {
            return "/bin/zsh";
        }

        return "/bin/bash";
    }
}
