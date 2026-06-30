using Celbridge.Settings.Platform;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Unit tests for the macOS Keychain credential store. These only run on macOS; the store reports itself
/// unavailable elsewhere. Constructing the store resolves every Security.framework constant, so this smoke
/// test also covers the native symbol resolution. The store/retrieve round-trip writes to the real login
/// Keychain, which can prompt across ad-hoc-signed rebuilds, so it is verified out of band rather than in
/// the shared suite.
/// </summary>
[TestFixture]
public class MacOSKeychainCredentialStoreTests
{
    [Test]
    [Platform("MacOsX")]
    public void IsAvailable_OnMacOS_IsTrue()
    {
        var store = new MacOSKeychainCredentialStore();

        store.IsAvailable.Should().BeTrue();
    }
}
