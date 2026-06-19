using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Covers the Protected scope of SettingsService: the Workshop Key round-trip
/// through encryption, presence checks that do not decrypt, reset, and the
/// failure modes (unavailable store, unprotect failure, corrupt ciphertext). The
/// protector is a reversing fake, so the assertions hold without DPAPI.
/// </summary>
[TestFixture]
public class ProtectedScopeTests
{
    private const string TestWorkshopKey = "kpf_abc123_supersecretvalue";

    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialProtector _protector = null!;
    private SettingsService _settingsService = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();
        _protector = new FakeCredentialProtector();

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        _settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            _settingsStore,
            _protector,
            workspaceWrapper);
    }

    [Test]
    public void IsScopeAvailable_Protected_MirrorsProtectorAvailability()
    {
        _protector.Available = true;
        _settingsService.IsScopeAvailable(SettingScope.Protected).Should().BeTrue();

        _protector.Available = false;
        _settingsService.IsScopeAvailable(SettingScope.Protected).Should().BeFalse();
    }

    [Test]
    public void SetThenTryGet_RoundTripsKey()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);

        var getResult = _settingsService.TryGet(SettingCatalog.Workshop.Key);

        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().Be(TestWorkshopKey);
    }

    [Test]
    public void Set_WritesCiphertextThatDoesNotContainPlaintext()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);

        // The protected value is stored under the descriptor key in the
        // application store; it must be base64 ciphertext, never the plaintext.
        _settingsStore.TryGetValue<string>(SettingCatalog.Workshop.Key.Key, out var storedValue).Should().BeTrue();
        storedValue.Should().NotBeEmpty();
        storedValue.Should().NotContain(TestWorkshopKey);
    }

    [Test]
    public void IsConfigured_FalseByDefault_TrueAfterSet_FalseAfterReset()
    {
        _settingsService.IsConfigured(SettingCatalog.Workshop.Key).Should().BeFalse();

        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _settingsService.IsConfigured(SettingCatalog.Workshop.Key).Should().BeTrue();

        _settingsService.Reset(SettingCatalog.Workshop.Key);
        _settingsService.IsConfigured(SettingCatalog.Workshop.Key).Should().BeFalse();
    }

    [Test]
    public void IsConfigured_DoesNotInvokeProtector()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);

        // An unprotect failure must not affect a presence check, since the check
        // never decrypts.
        _protector.FailUnprotect = true;

        _settingsService.IsConfigured(SettingCatalog.Workshop.Key).Should().BeTrue();
    }

    [Test]
    public void TryGet_Unavailable_Fails()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _protector.Available = false;

        var getResult = _settingsService.TryGet(SettingCatalog.Workshop.Key);

        getResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryGet_NothingStored_Fails()
    {
        var getResult = _settingsService.TryGet(SettingCatalog.Workshop.Key);

        getResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryGet_UnprotectFailure_FailsWithoutEchoingKey()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _protector.FailUnprotect = true;

        var getResult = _settingsService.TryGet(SettingCatalog.Workshop.Key);

        getResult.IsFailure.Should().BeTrue();
        getResult.MessageChain.Should().NotContain(TestWorkshopKey);
    }

    [Test]
    public void TryGet_InvalidBase64_FailsCleanly()
    {
        _settingsStore.SetValue(SettingCatalog.Workshop.Key.Key, "@@not-valid-base64@@");

        var getResult = _settingsService.TryGet(SettingCatalog.Workshop.Key);

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("could not be read");
    }

    [Test]
    public void Get_UnprotectFailure_FallsBackToDefault()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _protector.FailUnprotect = true;

        var value = _settingsService.Get(SettingCatalog.Workshop.Key);

        value.Should().Be(SettingCatalog.Workshop.Key.DefaultValue);
    }
}
