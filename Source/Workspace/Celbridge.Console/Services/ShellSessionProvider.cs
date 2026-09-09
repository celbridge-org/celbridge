using Celbridge.Utilities;

namespace Celbridge.Console.Services;

/// <summary>
/// The built-in "shell" session type. With no executable the session is just the platform shell; with one,
/// the executable and its arguments are injected as a command once the shell is up.
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
