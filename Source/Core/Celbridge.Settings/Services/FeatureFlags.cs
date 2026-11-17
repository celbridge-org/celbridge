using Celbridge.Messaging;
using Microsoft.Extensions.Configuration;

namespace Celbridge.Settings.Services;

/// <summary>
/// Implementation of IFeatureFlags that reads feature flags from configuration
/// and supports project-level overrides.
/// </summary>
public class FeatureFlags : IFeatureFlags
{
    private const string FeatureFlagKey = "FeatureFlags";

    private readonly IConfiguration _configuration;
    private readonly IMessengerService _messengerService;

    private IReadOnlyDictionary<string, bool> _projectOverrides = new Dictionary<string, bool>();

    private bool _allFeaturesOff;

    public FeatureFlags(
        IConfiguration configuration,
        IMessengerService messengerService)
    {
        _configuration = configuration;
        _messengerService = messengerService;
    }

    public bool IsEnabled(FeatureFlagDescriptor flag)
    {
        if (_allFeaturesOff)
        {
            return false;
        }

        return IsEnabledForProject(flag, _projectOverrides);
    }

    public bool IsEnabledForProject(FeatureFlagDescriptor flag, IReadOnlyDictionary<string, bool> projectOverrides)
    {
        if (projectOverrides.TryGetValue(flag.Name, out var overrideValue))
        {
            return overrideValue;
        }

        return GetApplicationValue(flag);
    }

    public bool GetApplicationValue(FeatureFlagDescriptor flag)
    {
        var section = _configuration.GetSection(FeatureFlagKey);
        var value = section[flag.Name];

        return bool.TryParse(value, out var isEnabled) && isEnabled;
    }

    public void ApplyProjectOverrides(IReadOnlyDictionary<string, bool> overrides)
    {
        _projectOverrides = overrides;
        _messengerService.Send(new FeatureFlagsChangedMessage());
    }

    public void TurnOffAllFeatures()
    {
        _allFeaturesOff = true;
        _messengerService.Send(new FeatureFlagsChangedMessage());
    }

    public void ClearProjectOverrides()
    {
        _projectOverrides = new Dictionary<string, bool>();
        _allFeaturesOff = false;
        _messengerService.Send(new FeatureFlagsChangedMessage());
    }
}
