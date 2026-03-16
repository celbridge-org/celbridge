using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.Settings.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Unit tests for FeatureFlagService focusing on override/clear behavior and message broadcasting.
/// </summary>
[TestFixture]
public class FeatureFlagsTests
{
    private IMessengerService _messengerService = null!;
    private FeatureFlagService _featureFlags = null!;

    [SetUp]
    public void Setup()
    {
        var configData = new Dictionary<string, string?>
        {
            ["FeatureFlags:console-panel"] = "true",
            ["FeatureFlags:note-editor"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IMessengerService, MessengerService>();
        var serviceProvider = services.BuildServiceProvider();

        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        _featureFlags = new FeatureFlagService(configuration, _messengerService);
    }

    #region App-Level Tests

    [Test]
    public void IsEnabled_EnabledInConfig_ReturnsTrue()
    {
        var result = _featureFlags.IsEnabled("console-panel");

        result.Should().BeTrue();
    }

    [Test]
    public void IsEnabled_DisabledInConfig_ReturnsFalse()
    {
        var result = _featureFlags.IsEnabled("note-editor");

        result.Should().BeFalse();
    }

    [Test]
    public void IsEnabled_NotConfigured_DefaultsToEnabled()
    {
        var result = _featureFlags.IsEnabled("unknown-feature");

        result.Should().BeTrue("features default to enabled when not configured");
    }

    #endregion

    #region Override Tests

    [Test]
    public void ApplyProjectOverrides_OverridesAppLevel()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled("note-editor").Should().BeTrue("project override should take precedence");
    }

    [Test]
    public void ApplyProjectOverrides_CanDisableEnabledFeature()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["console-panel"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled("console-panel").Should().BeFalse("project override should disable the feature");
    }

    [Test]
    public void ApplyProjectOverrides_NonOverriddenFeatures_FallBackToAppLevel()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled("console-panel").Should().BeTrue("non-overridden features should use app-level config");
    }

    [Test]
    public void ClearProjectOverrides_RevertsToAppLevel()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true,
            ["console-panel"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);
        _featureFlags.ClearProjectOverrides();

        _featureFlags.IsEnabled("note-editor").Should().BeFalse("should revert to app-level after clearing");
        _featureFlags.IsEnabled("console-panel").Should().BeTrue("should revert to app-level after clearing");
    }

    #endregion

    #region Message Tests

    [Test]
    public void ApplyProjectOverrides_SendsFeatureFlagsChangedMessage()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<FeatureFlagsChangedMessage>(recipient, (r, m) => messageReceived = true);

        _featureFlags.ApplyProjectOverrides(new Dictionary<string, bool>());

        messageReceived.Should().BeTrue();
    }

    [Test]
    public void ClearProjectOverrides_SendsFeatureFlagsChangedMessage()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<FeatureFlagsChangedMessage>(recipient, (r, m) => messageReceived = true);

        _featureFlags.ClearProjectOverrides();

        messageReceived.Should().BeTrue();
    }

    #endregion

    #region Multiple Features Tests

    [Test]
    public void IsEnabled_MultipleFeatures_HandlesEachIndependently()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true,
            ["console-panel"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled("note-editor").Should().BeTrue("project enables note-editor");
        _featureFlags.IsEnabled("console-panel").Should().BeFalse("project disables console-panel");
        _featureFlags.IsEnabled("unknown-feature").Should().BeTrue("defaults to enabled for unconfigured features");
    }

    #endregion
}
