using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.Settings.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Unit tests for FeatureFlags focusing on override/clear behavior and message broadcasting.
/// </summary>
[TestFixture]
public class FeatureFlagsTests
{
    private IMessengerService _messengerService = null!;
    private FeatureFlags _featureFlags = null!;

    [SetUp]
    public void Setup()
    {
        var configData = new Dictionary<string, string?>
        {
            ["FeatureFlags:mcp-tools"] = "true",
            ["FeatureFlags:note-editor"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IMessengerService, MessengerService>();
        var serviceProvider = services.BuildServiceProvider();

        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        _featureFlags = new FeatureFlags(configuration, _messengerService);
    }

    #region App-Level Tests

    [Test]
    public void IsEnabled_EnabledInConfig_ReturnsTrue()
    {
        var result = _featureFlags.IsEnabled("mcp-tools");

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
            ["mcp-tools"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled("mcp-tools").Should().BeFalse("project override should disable the feature");
    }

    [Test]
    public void ApplyProjectOverrides_NonOverriddenFeatures_FallBackToAppLevel()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled("mcp-tools").Should().BeTrue("non-overridden features should use app-level config");
    }

    [Test]
    public void ClearProjectOverrides_RevertsToAppLevel()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true,
            ["mcp-tools"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);
        _featureFlags.ClearProjectOverrides();

        _featureFlags.IsEnabled("note-editor").Should().BeFalse("should revert to app-level after clearing");
        _featureFlags.IsEnabled("mcp-tools").Should().BeTrue("should revert to app-level after clearing");
    }

    #endregion

    #region Build-Time Flag Tests

    [Test]
    public void IsEnabled_BuildTimeFlagNotConfigured_DefaultsToDisabled()
    {
        var buildTimeFlag = FeatureFlagConstants.BuildTimeFlags.First();

        _featureFlags.IsEnabled(buildTimeFlag).Should().BeFalse("a build-time flag is off unless the build turns it on");
    }

    [Test]
    public void IsEnabled_BuildTimeFlagEnabledInConfig_ReturnsTrue()
    {
        var buildTimeFlag = FeatureFlagConstants.BuildTimeFlags.First();
        var featureFlags = BuildFeatureFlags(new Dictionary<string, string?>
        {
            [$"FeatureFlags:{buildTimeFlag}"] = "true"
        });

        featureFlags.IsEnabled(buildTimeFlag).Should().BeTrue();
    }

    [Test]
    public void ApplyProjectOverrides_BuildTimeFlag_IsIgnored()
    {
        var buildTimeFlag = FeatureFlagConstants.BuildTimeFlags.First();
        var overrides = new Dictionary<string, bool>
        {
            [buildTimeFlag] = true
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled(buildTimeFlag).Should().BeFalse("a project cannot turn on a flag fixed at build time");
    }

    [Test]
    public void ApplyProjectOverrides_BuildTimeFlagEnabledByTheBuild_CannotBeDisabled()
    {
        var buildTimeFlag = FeatureFlagConstants.BuildTimeFlags.First();
        var featureFlags = BuildFeatureFlags(new Dictionary<string, string?>
        {
            [$"FeatureFlags:{buildTimeFlag}"] = "true"
        });

        featureFlags.ApplyProjectOverrides(new Dictionary<string, bool> { [buildTimeFlag] = false });

        featureFlags.IsEnabled(buildTimeFlag).Should().BeTrue("a project cannot turn off a flag fixed at build time");
    }

    private FeatureFlags BuildFeatureFlags(Dictionary<string, string?> configData)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new FeatureFlags(configuration, _messengerService);
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
            ["mcp-tools"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled("note-editor").Should().BeTrue("project enables note-editor");
        _featureFlags.IsEnabled("mcp-tools").Should().BeFalse("project disables mcp-tools");
        _featureFlags.IsEnabled("unknown-feature").Should().BeTrue("defaults to enabled for unconfigured features");
    }

    #endregion
}
