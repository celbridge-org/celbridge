namespace Celbridge.Console;

/// <summary>
/// Contributes environment variables to every console session before launch, so all console types share a
/// consistent environment. Runtime packages register implementations in DI.
/// </summary>
public interface IConsoleEnvironmentContributor
{
    /// <summary>
    /// Removes inherited variables that would undo this contributor's settings. Every console starts from the
    /// inherited environment.
    /// </summary>
    void FilterInheritedEnvironment(IDictionary<string, string> inheritedEnvironment);

    /// <summary>
    /// Adds or amends environment variables for a session about to launch. Called after the session
    /// provider builds its launch spec, so a variable the provider already set is visible here.
    /// </summary>
    Task ContributeAsync(ConsoleSessionContext context, IDictionary<string, string> environment);
}
