namespace Celbridge.Credentials;

/// <summary>
/// A Workshop server URL paired with the Application Key issued by that server.
/// The two values are stored and retrieved together because a key is only
/// meaningful against the server that issued it.
/// </summary>
public record WorkshopConnection(string WorkshopUrl, string ApplicationKey);

/// <summary>
/// Application-scoped store for sensitive credentials, encrypted at rest.
/// Stored values are retrievable only by host-side services through this typed
/// API and must never appear on agent-readable surfaces such as tool results,
/// log messages, the WebView, scripting APIs, or subprocess environments.
/// </summary>
public interface ICredentialService
{
    /// <summary>
    /// Returns true when the current platform provides a safe store for
    /// credentials. When false, all store operations fail and dependent
    /// features should degrade with a clear message.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the stored Workshop connection. Fails with an actionable message
    /// when no connection is stored or the stored entry cannot be read.
    /// </summary>
    Task<Result<WorkshopConnection>> GetWorkshopConnectionAsync();

    /// <summary>
    /// Stores the Workshop connection, replacing any existing one.
    /// </summary>
    Task<Result> SetWorkshopConnectionAsync(WorkshopConnection connection);

    /// <summary>
    /// Removes the stored Workshop connection. Succeeds when no connection is stored.
    /// </summary>
    Task<Result> ClearWorkshopConnectionAsync();
}
