namespace Celbridge.Credentials;

/// <summary>
/// A Workshop server URL, the Application Key issued by that server, and the
/// Author name recorded as the publisher of packages and pages.
/// </summary>
public record WorkshopConnection(string WorkshopUrl, string ApplicationKey, string Author = "");

/// <summary>
/// Summary of the stored Workshop connection, readable without decrypting it.
/// KeyHint is the identifying prefix of the stored Application Key, or empty
/// when the key has no recognisable prefix or the stored entry is unreadable.
/// </summary>
public record WorkshopConnectionSummary(bool IsStored, string KeyHint);

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
    /// Gets a summary of the stored Workshop connection without decrypting it,
    /// so display surfaces can identify the stored key. Reports a stored entry
    /// even when it is corrupt, so callers can offer clear and replace.
    /// </summary>
    Task<Result<WorkshopConnectionSummary>> GetWorkshopConnectionSummaryAsync();

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
