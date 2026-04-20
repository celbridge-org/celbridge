using Celbridge.Secrets;

namespace Celbridge.Tests.Helpers;

[TestFixture]
public class SecretRegistryTests
{
    [Test]
    public void TryResolve_SingleProvider_ReturnsProviderValue()
    {
        var provider = new SecretProvider(new Dictionary<string, Func<string>>
        {
            ["license"] = () => "abc123"
        });
        var registry = new SecretRegistry(new[] { (ISecretProvider)provider });

        registry.TryResolve("license").Should().Be("abc123");
        registry.TryResolve("missing").Should().BeNull();
    }

    [Test]
    public void TryResolve_MultipleProviders_UsesFirstMatchInOrder()
    {
        var first = new SecretProvider(new Dictionary<string, Func<string>>
        {
            ["shared"] = () => "from-first"
        });
        var second = new SecretProvider(new Dictionary<string, Func<string>>
        {
            ["shared"] = () => "from-second",
            ["only-second"] = () => "second-only"
        });
        var registry = new SecretRegistry(new ISecretProvider[] { first, second });

        registry.TryResolve("shared").Should().Be("from-first");
        registry.TryResolve("only-second").Should().Be("second-only");
    }

    [Test]
    public void ResolveAll_AllPresent_ReturnsPopulatedDictionary()
    {
        var provider = new SecretProvider(new Dictionary<string, Func<string>>
        {
            ["license"] = () => "abc123",
            ["designer_license"] = () => "def456"
        });
        var registry = new SecretRegistry(new[] { (ISecretProvider)provider });

        var result = registry.ResolveAll(new[] { "license", "designer_license" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value["license"].Should().Be("abc123");
        result.Value["designer_license"].Should().Be("def456");
    }

    [Test]
    public void ResolveAll_MissingName_ReturnsFailure()
    {
        var provider = new SecretProvider(new Dictionary<string, Func<string>>
        {
            ["known"] = () => "value"
        });
        var registry = new SecretRegistry(new[] { (ISecretProvider)provider });

        var result = registry.ResolveAll(new[] { "known", "unknown" });

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("unknown");
    }

    [Test]
    public void ResolveAll_EmptyNameRequest_IgnoresBlankEntries()
    {
        var provider = new SecretProvider(new Dictionary<string, Func<string>>
        {
            ["key"] = () => "value"
        });
        var registry = new SecretRegistry(new[] { (ISecretProvider)provider });

        var result = registry.ResolveAll(new[] { "", "key" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainKey("key").WhoseValue.Should().Be("value");
        result.Value.Should().HaveCount(1);
    }

    [Test]
    public void ResolveAll_NoProviders_ReturnsFailureWhenAnyRequested()
    {
        var registry = new SecretRegistry(Array.Empty<ISecretProvider>());

        var result = registry.ResolveAll(new[] { "anything" });

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void ResolveAll_EmptyRequest_ReturnsEmptyDictionary()
    {
        var registry = new SecretRegistry(Array.Empty<ISecretProvider>());

        var result = registry.ResolveAll(Array.Empty<string>());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
