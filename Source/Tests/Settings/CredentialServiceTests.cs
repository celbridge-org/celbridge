using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Settings;

/// <summary>
/// In-memory protector that reverses the payload bytes, so protected data
/// never matches the plaintext but round-trips exactly. Entropy is ignored;
/// production DPAPI enforces the binding, the fake does not.
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

/// <summary>
/// Unit tests for CredentialService covering the Workshop Key round-trip,
/// clearing, missing and corrupted entries, and availability. The service is
/// backed by a substitute IEditorSettings, since storage now lives in settings.
/// </summary>
[TestFixture]
public class CredentialServiceTests
{
    private const string TestWorkshopKey = "kpf_abc123_supersecretvalue";

    private IEditorSettings _editorSettings = null!;
    private FakeCredentialProtector _protector = null!;
    private CredentialService _credentialService = null!;

    [SetUp]
    public void Setup()
    {
        _editorSettings = Substitute.For<IEditorSettings>();
        // Initialize to empty so unseeded reads return "" rather than the
        // substitute's null default. The real EditorSettings returns "" too.
        _editorSettings.WorkshopKeyProtected = string.Empty;
        _editorSettings.WorkshopKeyHint = string.Empty;

        _protector = new FakeCredentialProtector();

        _credentialService = new CredentialService(
            new NullLogger<CredentialService>(),
            _protector,
            _editorSettings);
    }

    [Test]
    public void IsAvailable_MirrorsProtectorAvailability()
    {
        _protector.Available = true;
        _credentialService.IsAvailable.Should().BeTrue();

        _protector.Available = false;
        _credentialService.IsAvailable.Should().BeFalse();
    }

    [Test]
    public async Task AllOperations_FailWhenStoreIsUnavailable()
    {
        _protector.Available = false;

        var summaryResult = await _credentialService.GetWorkshopKeySummaryAsync();
        var getResult = await _credentialService.GetWorkshopKeyAsync();
        var setResult = await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);
        var clearResult = await _credentialService.ClearWorkshopKeyAsync();

        summaryResult.IsFailure.Should().BeTrue();
        getResult.IsFailure.Should().BeTrue();
        setResult.IsFailure.Should().BeTrue();
        clearResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task GetSummary_NothingStored_ReportsNotStored()
    {
        var summaryResult = await _credentialService.GetWorkshopKeySummaryAsync();

        summaryResult.IsSuccess.Should().BeTrue();

        var summary = summaryResult.Value;
        summary.IsStored.Should().BeFalse();
        summary.KeyHint.Should().BeEmpty();
    }

    [Test]
    public async Task GetSummary_StoredKey_ReportsKeyHint()
    {
        await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);

        var summaryResult = await _credentialService.GetWorkshopKeySummaryAsync();

        summaryResult.IsSuccess.Should().BeTrue();

        var summary = summaryResult.Value;
        summary.IsStored.Should().BeTrue();
        summary.KeyHint.Should().Be("kpf_abc123");
    }

    [Test]
    public async Task SetThenGet_RoundTripsKey()
    {
        var setResult = await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);
        setResult.IsSuccess.Should().BeTrue();

        var getResult = await _credentialService.GetWorkshopKeyAsync();
        getResult.IsSuccess.Should().BeTrue();

        getResult.Value.Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task Set_WritesNoKeyPlaintextToSettings()
    {
        await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);

        _editorSettings.WorkshopKeyProtected.Should().NotContain(TestWorkshopKey);
    }

    [Test]
    public async Task Set_StoresKeyDisplayHint()
    {
        await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);

        _editorSettings.WorkshopKeyHint.Should().Be("kpf_abc123");
    }

    [Test]
    public async Task Set_KeyWithUnexpectedShape_StoresEmptyHint()
    {
        await _credentialService.SetWorkshopKeyAsync("not-a-kpf-shaped-key");

        _editorSettings.WorkshopKeyHint.Should().BeEmpty();
    }

    [Test]
    public async Task Set_EmptyKey_Fails()
    {
        var emptyKeyResult = await _credentialService.SetWorkshopKeyAsync(string.Empty);

        emptyKeyResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Get_NothingStored_FailsWithActionableMessage()
    {
        var getResult = await _credentialService.GetWorkshopKeyAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("No Workshop Key is configured");
        getResult.FirstErrorMessage.Should().Contain("Settings page");
    }

    [Test]
    public async Task Get_InvalidBase64_FailsCleanly()
    {
        _editorSettings.WorkshopKeyProtected = "@@not-valid-base64@@";

        var getResult = await _credentialService.GetWorkshopKeyAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("could not be read");
    }

    [Test]
    public async Task Get_UnprotectFailure_FailsWithoutEchoingKey()
    {
        await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);
        _protector.FailUnprotect = true;

        var getResult = await _credentialService.GetWorkshopKeyAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.MessageChain.Should().NotContain(TestWorkshopKey);
    }

    [Test]
    public async Task Clear_RemovesStoredKey()
    {
        await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);

        var clearResult = await _credentialService.ClearWorkshopKeyAsync();
        clearResult.IsSuccess.Should().BeTrue();

        _editorSettings.WorkshopKeyProtected.Should().BeEmpty();
        _editorSettings.WorkshopKeyHint.Should().BeEmpty();

        var getResult = await _credentialService.GetWorkshopKeyAsync();
        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("No Workshop Key is configured");
    }

    [Test]
    public async Task Clear_WhenNothingStored_Succeeds()
    {
        var clearResult = await _credentialService.ClearWorkshopKeyAsync();

        clearResult.IsSuccess.Should().BeTrue();
    }
}
