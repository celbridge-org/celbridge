using StreamJsonRpc;

namespace Celbridge.Host;

public static class ResourceRpcMethods
{
    public const string Subscribe = "resources/subscribe";
    public const string Unsubscribe = "resources/unsubscribe";
    public const string Changed = "resources/changed";
}

/// <summary>
/// The kinds of change a resources/changed notification reports.
/// </summary>
public static class ResourceChangeKinds
{
    public const string Created = "created";
    public const string Changed = "changed";
    public const string Deleted = "deleted";
    public const string Renamed = "renamed";
}

/// <summary>
/// The params of a resources/changed notification. Resource keys are canonical ("project:src/main.py"), so a
/// page can hand them straight to the file tools. OldResource is set only for a rename.
/// </summary>
public sealed record ResourceChangeNotification(
    string Kind,
    string Resource,
    string? OldResource = null);

/// <summary>
/// The resource-change contract with a hosted page. A page that subscribes receives a resources/changed
/// notification for each file created, changed, deleted or renamed in the project tree, however the change
/// was made (an editor's save, an agent, another application). Nothing is sent until the page subscribes,
/// so editors that never ask pay nothing for it.
/// </summary>
public interface IHostResources
{
    /// <summary>
    /// Starts, or replaces, this page's subscription. Patterns use the project's resource glob syntax
    /// ("*.py" matches at any depth, "src/**/*.ts" from the project root). Null or empty subscribes to every
    /// file in the project.
    /// </summary>
    [JsonRpcMethod(ResourceRpcMethods.Subscribe)]
    void Subscribe(IReadOnlyList<string>? patterns = null);

    /// <summary>
    /// Ends this page's subscription. Changes still waiting to be reported are dropped.
    /// </summary>
    [JsonRpcMethod(ResourceRpcMethods.Unsubscribe)]
    void Unsubscribe();
}
