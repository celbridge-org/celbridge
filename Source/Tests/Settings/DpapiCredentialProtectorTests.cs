using System.Text;
using Celbridge.Settings.Services;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Unit tests for the Windows DPAPI credential protector. The DPAPI tests
/// only run on Windows; the protector reports itself unavailable elsewhere.
/// </summary>
[TestFixture]
public class DpapiCredentialProtectorTests
{
    private DpapiCredentialProtector _protector = null!;

    [SetUp]
    public void Setup()
    {
        _protector = new DpapiCredentialProtector();
    }

    [Test]
    [Platform("Win")]
    public void IsAvailable_OnWindows_IsTrue()
    {
        _protector.IsAvailable.Should().BeTrue();
    }

    [Test]
    [Platform("Win")]
    public void ProtectThenUnprotect_RoundTripsData()
    {
        var plainData = Encoding.UTF8.GetBytes("credential payload");

        var protectResult = _protector.Protect(plainData);
        protectResult.IsSuccess.Should().BeTrue();

        var protectedData = protectResult.Value;
        protectedData.Should().NotEqual(plainData);

        var unprotectResult = _protector.Unprotect(protectedData);
        unprotectResult.IsSuccess.Should().BeTrue();

        var roundTrippedData = unprotectResult.Value;
        roundTrippedData.Should().Equal(plainData);
    }

    [Test]
    [Platform("Win")]
    public void Unprotect_InvalidBlob_FailsWithoutThrowing()
    {
        var invalidBlob = new byte[] { 1, 2, 3, 4, 5 };

        var unprotectResult = _protector.Unprotect(invalidBlob);

        unprotectResult.IsFailure.Should().BeTrue();
    }
}
