namespace Celbridge.Secrets;

/// <summary>
/// Supplies named secrets to the host.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Attempts to resolve a secret by name. Returns null if not owned by this provider.
    /// </summary>
    string? TryResolve(string name);
}
