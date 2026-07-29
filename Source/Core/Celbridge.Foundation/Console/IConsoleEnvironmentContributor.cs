namespace Celbridge.Console;

/// <summary>
/// Contributes environment variables to every console session before launch, so all console types share a
/// consistent environment. Runtime packages register implementations in DI.
/// </summary>
public interface IConsoleEnvironmentContributor
{
    /// <summary>
    /// Adds or amends environment variables for a session about to launch. Called after the session
    /// provider builds its launch spec, so a variable the provider already set is visible here.
    /// </summary>
    Task ContributeAsync(ConsoleSessionContext context, IDictionary<string, string> environment);
}
