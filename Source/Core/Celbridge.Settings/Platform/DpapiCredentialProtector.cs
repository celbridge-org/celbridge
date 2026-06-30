using System.Security.Cryptography;

namespace Celbridge.Settings.Platform;

/// <summary>
/// Windows DPAPI encryption helper (CurrentUser scope). This is the internal crypto primitive composed by
/// DpapiCredentialStore; it encrypts and decrypts a blob but does not store it. Reports itself unavailable
/// on other platforms.
/// </summary>
internal sealed class DpapiCredentialProtector
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public Result<byte[]> Protect(byte[] plainData, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Fail("DPAPI credential protection is only available on Windows");
        }

        try
        {
            var protectedData = ProtectedData.Protect(plainData, entropy, DataProtectionScope.CurrentUser);

            return protectedData;
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail("Failed to protect credential data")
                .WithException(ex);
        }
    }

    public Result<byte[]> Unprotect(byte[] protectedData, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Fail("DPAPI credential protection is only available on Windows");
        }

        try
        {
            var plainData = ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser);

            return plainData;
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail("Failed to unprotect credential data")
                .WithException(ex);
        }
    }
}
