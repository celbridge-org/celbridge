using System.Security.Cryptography;

namespace Celbridge.Settings.Services;

/// <summary>
/// Windows credential protector backed by DPAPI with CurrentUser scope.
/// Reports itself unavailable on other platforms.
/// </summary>
internal sealed class DpapiCredentialProtector : ICredentialProtector
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public Result<byte[]> Protect(byte[] plainData)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Fail("DPAPI credential protection is only available on Windows");
        }

        try
        {
            var protectedData = ProtectedData.Protect(plainData, optionalEntropy: null, DataProtectionScope.CurrentUser);

            return protectedData;
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail("Failed to protect credential data")
                .WithException(ex);
        }
    }

    public Result<byte[]> Unprotect(byte[] protectedData)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Fail("DPAPI credential protection is only available on Windows");
        }

        try
        {
            var plainData = ProtectedData.Unprotect(protectedData, optionalEntropy: null, DataProtectionScope.CurrentUser);

            return plainData;
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail("Failed to unprotect credential data")
                .WithException(ex);
        }
    }
}
