using System.Reflection;
using Celbridge.Settings;

namespace Celbridge.Tests.Helpers;

/// <summary>
/// Reads each feature flag field of FeatureFlagCatalog, whether or not a group lists the flag.
/// </summary>
public static class DeclaredFeatureFlags
{
    public static IReadOnlyList<FeatureFlagDescriptor> Read()
    {
        var flags = typeof(FeatureFlagCatalog)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(FeatureFlagDescriptor))
            .Select(field => (FeatureFlagDescriptor)field.GetValue(null)!)
            .ToList();

        return flags;
    }
}
