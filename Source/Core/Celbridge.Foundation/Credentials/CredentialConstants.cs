namespace Celbridge.Credentials;

/// <summary>
/// Constants shared by the credential store and the surfaces that present it.
/// </summary>
public static class CredentialConstants
{
    /// <summary>
    /// The prefix of a well-formed Workshop Key, shaped like
    /// "kpf_(prefix)_(secret)". The prefix identifies the key and is not secret.
    /// </summary>
    public const string WorkshopKeyPrefix = "kpf_";
}
