namespace Celbridge.Settings.Services;

/// <summary>
/// Platform backend that encrypts and decrypts credential data at rest.
/// Each platform supplies its own implementation (DPAPI on Windows); where no
/// safe store exists the protector reports itself unavailable and the
/// credential service degrades rather than storing plaintext.
/// </summary>
internal interface ICredentialProtector
{
    /// <summary>
    /// Returns true when the current platform provides a safe store for
    /// protected data.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Encrypts the given data for the current user.
    /// </summary>
    Result<byte[]> Protect(byte[] plainData);

    /// <summary>
    /// Decrypts data previously returned by Protect for the same user.
    /// </summary>
    Result<byte[]> Unprotect(byte[] protectedData);
}
