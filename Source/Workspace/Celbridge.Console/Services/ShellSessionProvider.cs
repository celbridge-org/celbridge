using Celbridge.Utilities;

namespace Celbridge.Console.Services;

/// <summary>
/// The built-in "shell" session type. Without an executable, the session is just the platform's shell. With
/// one, the shell runs the executable and its arguments as the console's command.
/// </summary>
public sealed class ShellSessionProvider : IConsoleSessionProvider
{
    private const string ExecutableKey = "executable";
    private const string ArgumentsKey = "arguments";

    public ConsoleSessionType SessionType { get; } = new(
        "shell",
        OptionKeys: new[] { ExecutableKey, ArgumentsKey },
        BuiltInRunners: Array.Empty<ConsoleRunner>());

    public async Task<Result<ConsoleStartupInvocation>> BuildStartupInvocationAsync(ConsoleSessionContext context)
    {
        await Task.CompletedTask;

        var executable = ConfigTableHelper.ReadText(context.SessionTypeOptions, ExecutableKey);
        if (string.IsNullOrWhiteSpace(executable))
        {
            return ConsoleStartupInvocation.None;
        }

        var arguments = ConfigTableHelper.ReadTextList(context.SessionTypeOptions, ArgumentsKey);

        var startupInvocation = new ConsoleStartupInvocation(executable, arguments);
        return startupInvocation;
    }
}
