using Microsoft.Extensions.Configuration;

namespace Celbridge.Settings.Services;

/// <summary>
/// Implementation of IFeatureFlagService that reads feature flags from configuration.
/// </summary>
public class FeatureFlagService : IFeatureFlagService
{
    private const string FeatureFlagKey = "FeatureFlags";

    private readonly IConfiguration _configuration;

    public FeatureFlagService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsEnabled(string featureName)
    {
        var section = _configuration.GetSection(FeatureFlagKey);
        var value = section[featureName];

        if (string.IsNullOrEmpty(value))
        {
            // Features default to enabled when not configured.
            // Only an explicit "false" disables a feature.
            return true;
        }

        return !bool.TryParse(value, out var result) || result;
    }
}
