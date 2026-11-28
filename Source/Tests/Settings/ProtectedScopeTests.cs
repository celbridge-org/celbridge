using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Covers the Protected scope of SettingsService: a secret's round-trip through the credential store,
/// presence checks that do not retrieve, reset, and the failure modes (unavailable store, retrieve failure).
/// The credential store is an in-memory fake. The encrypt-and-persist behaviour of the real Windows store is
/// covered by DpapiCredentialStoreTests.
/// </summary>
[TestFixture]
public class ProtectedScopeTests
{
    private const string TestSecretValue = "abc123_supersecretvalue";

    private static readonly SettingDescriptor<string> TestSecret =
        new("Test.Secret", SettingScope.Protected, "");

    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialStore _credentialStore = null!;
    private SettingsService _settingsService = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();
        _credentialStore = new FakeCredentialStore();

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(false);

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
        _settingsService.Set(TestSecret, TestSecretValue);

        var getResult = _settingsService.TryGet(TestSecret);

        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().Be(TestSecretValue);
    }

    [Test]
    public void Set_DoesNotWriteSecretToTheSettingsStore()
    {
        _settingsService.Set(TestSecret, TestSecretValue);

        // Protected secrets live in the credential store, never in the application settings store.
        _settingsStore.ContainsKey(TestSecret.Key).Should().BeFalse();
    }

    [Test]
    public void IsConfigured_FalseByDefault_TrueAfterSet_FalseAfterReset()
    {
        _settingsService.IsConfigured(TestSecret).Should().BeFalse();

        _settingsService.Set(TestSecret, TestSecretValue);
        _settingsService.IsConfigured(TestSecret).Should().BeTrue();

        _settingsService.Reset(TestSecret);
        _settingsService.IsConfigured(TestSecret).Should().BeFalse();
    }

    [Test]
    public void IsConfigured_DoesNotRetrieve()
    {
        _settingsService.Set(TestSecret, TestSecretValue);

        // A retrieve failure must not affect a presence check, since the check never retrieves the secret.
        _credentialStore.FailRetrieve = true;

        _settingsService.IsConfigured(TestSecret).Should().BeTrue();
    }

    [Test]
    public void TryGet_Unavailable_Fails()
    {
        _settingsService.Set(TestSecret, TestSecretValue);
        _credentialStore.Available = false;

        var getResult = _settingsService.TryGet(TestSecret);

        getResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryGet_NothingStored_Fails()
    {
        var getResult = _settingsService.TryGet(TestSecret);

        getResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void TryGet_RetrieveFailure_FailsWithoutEchoingSecret()
    {
        _settingsService.Set(TestSecret, TestSecretValue);
        _credentialStore.FailRetrieve = true;

        var getResult = _settingsService.TryGet(TestSecret);

        getResult.IsFailure.Should().BeTrue();
        getResult.MessageChain.Should().NotContain(TestSecretValue);
    }

    [Test]
    public void Get_RetrieveFailure_FallsBackToDefault()
    {
        _settingsService.Set(TestSecret, TestSecretValue);
        _credentialStore.FailRetrieve = true;

        var value = _settingsService.Get(TestSecret);

        value.Should().Be(TestSecret.DefaultValue);
    }
}
