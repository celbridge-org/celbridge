using Celbridge.Settings.Services;

namespace Celbridge.Tests.Settings;

/// <summary>
/// In-memory protector that reverses the payload bytes, so protected data never
/// matches the plaintext but round-trips exactly. Entropy is ignored; production
/// DPAPI enforces the binding, the fake does not.
/// </summary>
public sealed class FakeCredentialProtector : ICredentialProtector
{
    public bool Available { get; set; } = true;

    public bool FailUnprotect { get; set; }

    public bool IsAvailable => Available;

    public Result<byte[]> Protect(byte[] plainData, byte[] entropy)
    {
        var protectedData = plainData.Reverse().ToArray();

        return protectedData;
    }

    public Result<byte[]> Unprotect(byte[] protectedData, byte[] entropy)
    {
        if (FailUnprotect)
        {
            return Result.Fail("Simulated unprotect failure");
        }

        var plainData = protectedData.Reverse().ToArray();

        return plainData;
    }
}
