namespace Celbridge.Console;

/// <summary>
/// Contributes environment variables to every console session before launch, so all console types share a
/// consistent environment. Runtime packages register implementations in DI.
/// </summary>
public interface IConsoleEnvironmentContributor
{
    /// <summary>
    /// Removes the variables that would undo what this contributor sets up from the environment the
    /// application inherited, which every console starts from.
    /// </summary>
    void FilterInheritedEnvironment(IDictionary<string, string> inheritedEnvironment);

    /// <summary>
    /// Adds or amends environment variables for a session about to launch. Called after the session
    /// provider builds its launch spec, so a variable the provider already set is visible here.
    /// </summary>
    Task ContributeAsync(ConsoleSessionContext context, IDictionary<string, string> environment);
}
