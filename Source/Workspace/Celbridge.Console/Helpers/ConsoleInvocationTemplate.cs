namespace Celbridge.Console.Helpers;

/// <summary>
/// The command template a runner or a trigger is configured with, and the substitution that turns one into
/// the invocation submitted to a console.
/// </summary>
public static class ConsoleInvocationTemplate
{
    /// <summary>
    /// The placeholder a template substitutes a resource path for: the file being run for a runner, the
    /// file that changed for a trigger.
    /// </summary>
    public const string ResourcePlaceholder = "{resource}";

    /// <summary>
    /// Returns the template with the resource placeholder replaced by a resource path.
    /// </summary>
    public static string Substitute(string commandTemplate, string resourcePath)
    {
        return commandTemplate.Replace(ResourcePlaceholder, resourcePath);
    }
}
