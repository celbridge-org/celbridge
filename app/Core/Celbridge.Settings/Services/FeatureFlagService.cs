using Microsoft.Extensions.Configuration;

namespace Celbridge.Settings.Services;

/// <summary>
/// Implementation of IFeatureFlagService that reads feature flags from configuration.
/// </summary>
public class FeatureFlagService : IFeatureFlagService
{
    private readonly IConfiguration _configuration;

    public FeatureFlagService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool IsEnabled(string featureName)
    {
        var section = _configuration.GetSection("FeatureFlags");
        var value = section[featureName];

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return bool.TryParse(value, out var result) && result;
    }
}
