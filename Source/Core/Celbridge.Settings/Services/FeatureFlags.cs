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

    public FeatureFlags(
        IConfiguration configuration,
        IMessengerService messengerService)
    {
        _configuration = configuration;
        _messengerService = messengerService;
    }

    public bool IsEnabled(string featureName)
    {
        // The surfaces a non-overridable flag gates are live before a project is loaded, so there is no
        // project whose answer they could follow.
        if (!FeatureFlagConstants.NonOverridableFlags.Contains(featureName) &&
            _projectOverrides.TryGetValue(featureName, out var overrideValue))
        {
            return overrideValue;
        }

        return GetApplicationValue(featureName);
    }

    public bool GetApplicationValue(string featureName)
    {
        var section = _configuration.GetSection(FeatureFlagKey);
        var value = section[featureName];

        // A missing entry, or one that is not a boolean, resolves to off, so a flag the build never
        // configured exposes nothing.
        return bool.TryParse(value, out var isEnabled) && isEnabled;
    }

    public void ApplyProjectOverrides(IReadOnlyDictionary<string, bool> overrides)
    {
        _projectOverrides = overrides;
        _messengerService.Send(new FeatureFlagsChangedMessage());
    }

    public void ClearProjectOverrides()
    {
        _projectOverrides = new Dictionary<string, bool>();
        _messengerService.Send(new FeatureFlagsChangedMessage());
    }
}
