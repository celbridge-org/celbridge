namespace Celbridge.Console.Services;

/// <summary>
/// The built-in "shell" session type. With no executable the session is just the platform shell; with one,
/// the executable and its arguments are injected as a command once the shell is up.
/// </summary>
public sealed class ShellSessionProvider : IConsoleSessionProvider
{
    public string TypeId => "shell";

    public IReadOnlyList<ConsoleRunner> DefaultRunners => Array.Empty<ConsoleRunner>();

    public async Task<Result<ConsoleStartupInvocation>> BuildStartupInvocationAsync(ConsoleSessionContext context)
    {
        await Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(context.Executable))
        {
            return ConsoleStartupInvocation.None;
        }

        var startupInvocation = new ConsoleStartupInvocation(context.Executable, context.Arguments);
        return startupInvocation;
    }
}
