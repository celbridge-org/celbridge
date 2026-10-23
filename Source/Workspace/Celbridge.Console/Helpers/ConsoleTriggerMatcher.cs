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
/// Resolves the commands a batch of changes fires for a trigger.
/// </summary>
public static class ConsoleTriggerMatcher
{
    /// <summary>
    /// Returns one command for each file in the batch that matches the trigger's pattern. Folders and absent keys fire
    /// nothing. Triggers watch only the project, so a batch from another root fires nothing.
    /// </summary>
    public static IReadOnlyList<string> Resolve(ConsoleTrigger trigger, ResourceChangeBatch batch)
    {
        var invocations = new List<string>();

        if (batch.RootName != ResourceKey.DefaultRoot)
        {
            return invocations;
        }

        foreach (var change in batch.Changes)
        {
            if (change.State != ResourceItemState.File
                || !trigger.Matcher.IsMatch(change.Resource.Path, isFolder: false))
            {
                continue;
            }

            var invocation = ConsoleInvocationTemplate.Substitute(trigger.CommandTemplate, change.Resource.Path);
            invocations.Add(invocation);
        }

        return invocations;
    }
}
