using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Covers the Protected scope of SettingsService: the Workshop Key round-trip through the credential store,
/// presence checks that do not retrieve, reset, and the failure modes (unavailable store, retrieve failure).
/// The credential store is an in-memory fake; the encrypt-and-persist behaviour of the real Windows store is
/// covered by DpapiCredentialStoreTests.
/// </summary>
[TestFixture]
public class ProtectedScopeTests
{
    private const string TestWorkshopKey = "kpf_abc123_supersecretvalue";

    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialStore _credentialStore = null!;
    private SettingsService _settingsService = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();
        _credentialStore = new FakeCredentialStore();

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        _settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            _settingsStore,
            _credentialStore,
            workspaceWrapper);
    }

    [Test]
    public void IsScopeAvailable_Protected_MirrorsStoreAvailability()
    {
        _credentialStore.Available = true;
        _settingsService.IsScopeAvailable(SettingScope.Protected).Should().BeTrue();

        _credentialStore.Available = false;
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
    public void Set_DoesNotWriteSecretToTheSettingsStore()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);

        // Protected secrets live in the credential store, never in the application settings store.
        _settingsStore.ContainsKey(SettingCatalog.Workshop.Key.Key).Should().BeFalse();
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
    public void IsConfigured_DoesNotRetrieve()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);

        // A retrieve failure must not affect a presence check, since the check never retrieves the secret.
        _credentialStore.FailRetrieve = true;

        _settingsService.IsConfigured(SettingCatalog.Workshop.Key).Should().BeTrue();
    }

    [Test]
    public void TryGet_Unavailable_Fails()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _credentialStore.Available = false;

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
    public void TryGet_RetrieveFailure_FailsWithoutEchoingSecret()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _credentialStore.FailRetrieve = true;

        var getResult = _settingsService.TryGet(SettingCatalog.Workshop.Key);

        getResult.IsFailure.Should().BeTrue();
        getResult.MessageChain.Should().NotContain(TestWorkshopKey);
    }

    [Test]
    public void Get_RetrieveFailure_FallsBackToDefault()
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _credentialStore.FailRetrieve = true;

        var value = _settingsService.Get(SettingCatalog.Workshop.Key);

        value.Should().Be(SettingCatalog.Workshop.Key.DefaultValue);
    }
}
