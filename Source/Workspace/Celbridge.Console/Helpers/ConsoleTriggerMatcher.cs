using Celbridge.Utilities;

namespace Celbridge.Console.Helpers;

/// <summary>
/// A trigger compiled for a running session: the pattern a changed resource must match, and the command
/// template injected when it does.
/// </summary>
public sealed record ConsoleTrigger(
    ResourcePathMatcher Matcher,
    string CommandTemplate);

/// <summary>
/// Resolves the commands a resource change fires in a console session.
/// </summary>
public static class ConsoleTriggerMatcher
{
    /// <summary>
    /// The placeholder a trigger command substitutes the changed resource's path for.
    /// </summary>
    public const string ResourcePlaceholder = "{resource}";

    /// <summary>
    /// Returns the resolved commands for every trigger a changed resource matches. Triggers watch the
    /// project tree only, so a resource under any other root matches nothing.
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<ConsoleTrigger> triggers,
        ResourceKey resource)
    {
        var invocations = new List<string>();

        if (resource.IsEmpty ||
            resource.Root != ResourceKey.DefaultRoot)
        {
            return invocations;
        }

        foreach (var trigger in triggers)
        {
            if (!trigger.Matcher.IsMatch(resource.Path, isFolder: false))
            {
                continue;
            }

            var invocation = trigger.CommandTemplate.Replace(ResourcePlaceholder, resource.Path);
            invocations.Add(invocation);
        }

        return invocations;
    }
}
