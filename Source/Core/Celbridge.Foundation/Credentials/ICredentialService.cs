namespace Celbridge.Credentials;

/// <summary>
/// Summary of the stored Workshop Key, readable without decrypting it.
/// KeyHint is the identifying prefix of the stored key, or empty when the key
/// has no recognisable prefix or the stored entry is unreadable.
/// </summary>
public record WorkshopKeySummary(bool IsStored, string KeyHint);

/// <summary>
/// Application-scoped store for secret credentials, encrypted at rest. The store
/// is general purpose, with one typed accessor per credential. Stored values are
/// retrievable only by host-side services through this typed API and must never
/// appear on agent-readable surfaces such as tool results, log messages, the
/// WebView, scripting APIs, or subprocess environments. Only secrets belong here;
/// non-secret configuration belongs in settings.
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
    /// Gets a summary of the stored Workshop Key without decrypting it, so
    /// display surfaces can identify the stored key. Reports a stored entry even
    /// when it is corrupt, so callers can offer clear and replace.
    /// </summary>
    Task<Result<WorkshopKeySummary>> GetWorkshopKeySummaryAsync();

    /// <summary>
    /// Gets the stored Workshop Key. Fails with an actionable message when no
    /// key is stored or the stored entry cannot be read.
    /// </summary>
    Task<Result<string>> GetWorkshopKeyAsync();

    /// <summary>
    /// Stores the Workshop Key, replacing any existing one.
    /// </summary>
    Task<Result> SetWorkshopKeyAsync(string workshopKey);

    /// <summary>
    /// Removes the stored Workshop Key. Succeeds when none is stored.
    /// </summary>
    Task<Result> ClearWorkshopKeyAsync();
}
