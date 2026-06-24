using System.Text;

namespace Celbridge.Settings.Services;

/// <summary>
/// Windows credential store: encrypts the secret with DPAPI and persists the ciphertext in the application
/// settings store, keyed by the credential key. The DPAPI entropy is derived from the key so each credential
/// is bound to its own slot. Reports itself unavailable on other platforms.
/// </summary>
internal sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly DpapiCredentialProtector _protector;
    private readonly ISettingsStore _applicationStore;

    public DpapiCredentialStore(ISettingsStore applicationStore)
    {
        _protector = new DpapiCredentialProtector();
        _applicationStore = applicationStore;
    }

    public bool IsAvailable => _protector.IsAvailable;

    public Result StoreCredential(string key, byte[] secret)
    {
        var entropy = Encoding.UTF8.GetBytes(key);

        var protectResult = _protector.Protect(secret, entropy);
        if (protectResult.IsFailure)
        {
            return Result.Fail($"Failed to store credential '{key}'")
                .WithErrors(protectResult);
        }

        var base64 = Convert.ToBase64String(protectResult.Value);
        _applicationStore.SetValue(key, base64);

        return Result.Ok();
    }

    public Result<byte[]> RetrieveCredential(string key)
    {
        if (!_applicationStore.TryGetValue<string>(key, out var base64)
            || string.IsNullOrEmpty(base64))
        {
            return Result<byte[]>.Fail($"No credential is stored for '{key}'");
        }

        byte[] protectedData;
        try
        {
            protectedData = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return Result<byte[]>.Fail("A stored credential could not be read");
        }

        var entropy = Encoding.UTF8.GetBytes(key);

        var unprotectResult = _protector.Unprotect(protectedData, entropy);
        if (unprotectResult.IsFailure)
        {
            return Result<byte[]>.Fail("A stored credential could not be read")
                .WithErrors(unprotectResult);
        }

        return unprotectResult.Value;
    }

    public bool ContainsCredential(string key)
    {
        // The ciphertext lives in the application store, so presence is a key lookup that never decrypts.
        return _applicationStore.ContainsKey(key);
    }

    public Result DeleteCredential(string key)
    {
        _applicationStore.RemoveValue(key);

        return Result.Ok();
    }
}
