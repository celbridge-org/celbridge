namespace Celbridge.Settings.Services;

/// <summary>
/// Secure store for named credentials: keeps a secret under a key and retrieves it later. When no safe store
/// is available the implementation reports itself unavailable, and the credential service degrades rather than
/// falling back to plaintext.
/// </summary>
internal interface ICredentialStore
{
    /// <summary>
    /// True when the current platform provides a safe store for credentials.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Stores the secret bytes under the given key, replacing any existing value.
    /// </summary>
    Result StoreCredential(string key, byte[] secret);

    /// <summary>
    /// Retrieves the secret bytes previously stored under the key. Fails when no credential is stored.
    /// </summary>
    Result<byte[]> RetrieveCredential(string key);

    /// <summary>
    /// Returns whether a credential is stored under the key, without retrieving (or decrypting) the secret.
    /// </summary>
    bool ContainsCredential(string key);

    /// <summary>
    /// Removes the credential stored under the key. A no-op when nothing is stored.
    /// </summary>
    Result DeleteCredential(string key);
}
