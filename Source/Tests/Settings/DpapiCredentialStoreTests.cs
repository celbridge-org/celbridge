using System.Text;
using Celbridge.Settings.Platform;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Unit tests for the Windows DPAPI credential store: the encrypt-and-persist behaviour over an in-memory
/// settings store. These only run on Windows; the store reports itself unavailable elsewhere.
/// </summary>
[TestFixture]
public class DpapiCredentialStoreTests
{
    private const string TestKey = "Test.WorkshopKey";
    private static readonly byte[] TestSecret = Encoding.UTF8.GetBytes("kpf_abc123_supersecretvalue");

    private FakeSettingsStore _settingsStore = null!;
    private DpapiCredentialStore _store = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();
        _store = new DpapiCredentialStore(_settingsStore);
    }

    [Test]
    [Platform("Win")]
    public void IsAvailable_OnWindows_IsTrue()
    {
        _store.IsAvailable.Should().BeTrue();
    }

    [Test]
    [Platform("Win")]
    public void StoreThenRetrieve_RoundTripsSecret()
    {
        _store.StoreCredential(TestKey, TestSecret).IsSuccess.Should().BeTrue();

        var retrieveResult = _store.RetrieveCredential(TestKey);

        retrieveResult.IsSuccess.Should().BeTrue();
        retrieveResult.Value.Should().Equal(TestSecret);
    }

    [Test]
    [Platform("Win")]
    public void Store_PersistsCiphertextNotPlaintext()
    {
        _store.StoreCredential(TestKey, TestSecret);

        // The value persisted under the key must be base64 ciphertext, never the plaintext.
        _settingsStore.TryGetValue<string>(TestKey, out var storedValue).Should().BeTrue();
        storedValue.Should().NotBeEmpty();
        storedValue.Should().NotContain(Encoding.UTF8.GetString(TestSecret));
    }

    [Test]
    [Platform("Win")]
    public void Retrieve_InvalidBase64_FailsCleanly()
    {
        _settingsStore.SetValue(TestKey, "@@not-valid-base64@@");

        var retrieveResult = _store.RetrieveCredential(TestKey);

        retrieveResult.IsFailure.Should().BeTrue();
        retrieveResult.FirstErrorMessage.Should().Contain("could not be read");
    }

    [Test]
    [Platform("Win")]
    public void RetrieveNothingStored_Fails()
    {
        _store.RetrieveCredential(TestKey).IsFailure.Should().BeTrue();
    }

    [Test]
    [Platform("Win")]
    public void ContainsCredential_TracksStoreAndDelete()
    {
        _store.ContainsCredential(TestKey).Should().BeFalse();

        _store.StoreCredential(TestKey, TestSecret);
        _store.ContainsCredential(TestKey).Should().BeTrue();

        _store.DeleteCredential(TestKey).IsSuccess.Should().BeTrue();
        _store.ContainsCredential(TestKey).Should().BeFalse();
    }
}
