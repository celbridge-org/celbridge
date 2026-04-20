namespace Celbridge.Secrets;

/// <summary>
/// Aggregates all registered ISecretProvider instances and resolves named secrets.
/// </summary>
public interface ISecretRegistry
{
    /// <summary>
    /// Resolves a single secret by name. Returns null if no provider owns it.
    /// </summary>
    string? TryResolve(string name);

    /// <summary>
    /// Resolves a batch of secrets by name. Fails if any name cannot be resolved.
    /// </summary>
    Result<IReadOnlyDictionary<string, string>> ResolveAll(IEnumerable<string> names);
}
