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
    /// Encrypts the given data for the current user. The entropy byte array is
    /// bound into the ciphertext; the same value must be supplied to Unprotect.
    /// Each credential type uses its own fixed entropy so a different DPAPI
    /// consumer running as the same user cannot decrypt our data by accident.
    /// </summary>
    Result<byte[]> Protect(byte[] plainData, byte[] entropy);

    /// <summary>
    /// Decrypts data previously returned by Protect for the same user. The
    /// entropy must match the value passed to Protect; otherwise unprotection
    /// fails.
    /// </summary>
    Result<byte[]> Unprotect(byte[] protectedData, byte[] entropy);
}
