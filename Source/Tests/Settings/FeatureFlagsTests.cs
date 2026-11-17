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
    // Flags the catalog does not declare, with names that the test configuration leaves out or sets to a non-boolean.
    private static readonly FeatureFlagDescriptor UnknownFlag = new("unknown-feature", string.Empty, string.Empty);
    private static readonly FeatureFlagDescriptor MisconfiguredFlag = new("misconfigured-feature", string.Empty, string.Empty);

    private IMessengerService _messengerService = null!;
    private FeatureFlags _featureFlags = null!;

    [SetUp]
    public void Setup()
    {
        var configData = new Dictionary<string, string?>
        {
            ["FeatureFlags:webview-dev-tools"] = "true",
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

    private FeatureFlags BuildFeatureFlags(Dictionary<string, string?> configData)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new FeatureFlags(configuration, _messengerService);
    }

    #region App-Level Tests

    [Test]
    public void IsEnabled_EnabledInConfig_ReturnsTrue()
    {
        var result = _featureFlags.IsEnabled(FeatureFlagCatalog.WebViewDevTools);

        result.Should().BeTrue();
    }

    [Test]
    public void IsEnabled_DisabledInConfig_ReturnsFalse()
    {
        var result = _featureFlags.IsEnabled(FeatureFlagCatalog.NoteEditor);

        result.Should().BeFalse();
    }

    [Test]
    public void IsEnabled_NotConfigured_DefaultsToDisabled()
    {
        var result = _featureFlags.IsEnabled(UnknownFlag);

        result.Should().BeFalse("a flag the build does not list is off");
    }

    [Test]
    public void IsEnabled_NonBooleanValue_ReturnsFalse()
    {
        var featureFlags = BuildFeatureFlags(new Dictionary<string, string?>
        {
            ["FeatureFlags:misconfigured-feature"] = "yes"
        });

        featureFlags.IsEnabled(MisconfiguredFlag).Should().BeFalse("a value that is not a boolean resolves to off");
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

        _featureFlags.IsEnabled(FeatureFlagCatalog.NoteEditor).Should().BeTrue("project override should take precedence");
    }

    [Test]
    public void ApplyProjectOverrides_CanDisableEnabledFeature()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["webview-dev-tools"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled(FeatureFlagCatalog.WebViewDevTools).Should().BeFalse("project override should disable the feature");
    }

    [Test]
    public void ApplyProjectOverrides_NonOverriddenFeatures_FallBackToAppLevel()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled(FeatureFlagCatalog.WebViewDevTools).Should().BeTrue("non-overridden features should use app-level config");
    }

    [Test]
    public void ClearProjectOverrides_RevertsToAppLevel()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true,
            ["webview-dev-tools"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);
        _featureFlags.ClearProjectOverrides();

        _featureFlags.IsEnabled(FeatureFlagCatalog.NoteEditor).Should().BeFalse("should revert to app-level after clearing");
        _featureFlags.IsEnabled(FeatureFlagCatalog.WebViewDevTools).Should().BeTrue("should revert to app-level after clearing");
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
            ["webview-dev-tools"] = false
        };

        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.IsEnabled(FeatureFlagCatalog.NoteEditor).Should().BeTrue("project enables note-editor");
        _featureFlags.IsEnabled(FeatureFlagCatalog.WebViewDevTools).Should().BeFalse("project disables webview-dev-tools");
        _featureFlags.IsEnabled(UnknownFlag).Should().BeFalse("unconfigured features default to off");
    }

    #endregion

    [Test]
    public void TurnOffAllFeatures_TurnsOffADefaultAndAProjectOverride()
    {
        var overrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true
        };
        _featureFlags.ApplyProjectOverrides(overrides);

        _featureFlags.TurnOffAllFeatures();

        _featureFlags.IsEnabled(FeatureFlagCatalog.WebViewDevTools).Should().BeFalse("the application turns the flag on by default");
        _featureFlags.IsEnabled(FeatureFlagCatalog.NoteEditor).Should().BeFalse("the project turns the flag on");
    }

    [Test]
    public void TurnOffAllFeatures_LeavesTheApplicationValues()
    {
        _featureFlags.TurnOffAllFeatures();

        _featureFlags.GetApplicationValue(FeatureFlagCatalog.WebViewDevTools).Should().BeTrue();
    }

    [Test]
    public void ClearProjectOverrides_TurnsTheFeaturesBackOn()
    {
        _featureFlags.TurnOffAllFeatures();

        _featureFlags.ClearProjectOverrides();

        _featureFlags.IsEnabled(FeatureFlagCatalog.WebViewDevTools).Should().BeTrue();
    }

    [Test]
    public void IsEnabledForProject_TheProjectsValueWins_AndAnyOtherFlagTakesTheApplicationValue()
    {
        var projectOverrides = new Dictionary<string, bool>
        {
            ["note-editor"] = true
        };

        _featureFlags.IsEnabledForProject(FeatureFlagCatalog.NoteEditor, projectOverrides).Should().BeTrue();
        _featureFlags.IsEnabledForProject(FeatureFlagCatalog.WebViewDevTools, projectOverrides).Should().BeTrue();
        _featureFlags.IsEnabledForProject(UnknownFlag, projectOverrides).Should().BeFalse();
    }

    [Test]
    public void IsEnabledForProject_IgnoresTheAppliedOverridesAndTurnOffAllFeatures()
    {
        var appliedOverrides = new Dictionary<string, bool>
        {
            ["webview-dev-tools"] = false
        };
        _featureFlags.ApplyProjectOverrides(appliedOverrides);
        _featureFlags.TurnOffAllFeatures();

        var isEnabled = _featureFlags.IsEnabledForProject(FeatureFlagCatalog.WebViewDevTools, new Dictionary<string, bool>());

        isEnabled.Should().BeTrue("the application turns the flag on, and the project being loaded leaves it alone");
    }

    [Test]
    public void TurnOffAllFeatures_SendsFeatureFlagsChangedMessage()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<FeatureFlagsChangedMessage>(recipient, (r, m) => messageReceived = true);

        _featureFlags.TurnOffAllFeatures();

        messageReceived.Should().BeTrue();
    }
}
