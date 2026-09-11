using System.Reflection;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Settings;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Keeps FeatureFlagCatalog (the Project Settings UI metadata) in sync with FeatureFlagConstants (the
/// canonical flag names), so adding a flag to one without the other fails the build rather than silently
/// leaving a gap in the panel. Non-overridable flags are the exception: the panel sets a project
/// override, which one of those ignores, so listing it would offer the user a toggle that does nothing.
/// </summary>
[TestFixture]
public class FeatureFlagCatalogTests
{
    private static IReadOnlyList<string> GetConstantFlagNames()
    {
        return typeof(FeatureFlagConstants)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
    }

    [Test]
    public void Catalog_CoversEveryRuntimeFeatureFlag()
    {
        var runtimeNames = GetConstantFlagNames()
            .Where(flagName => !FeatureFlagConstants.NonOverridableFlags.Contains(flagName))
            .ToList();
        var catalogNames = FeatureFlagCatalog.Descriptors.Select(descriptor => descriptor.FlagName).ToList();

        catalogNames.Should().BeEquivalentTo(runtimeNames);
    }

    [Test]
    public void Catalog_ExcludesNonOverridableFlags()
    {
        var catalogNames = FeatureFlagCatalog.Descriptors.Select(descriptor => descriptor.FlagName).ToList();

        catalogNames.Should().NotIntersectWith(FeatureFlagConstants.NonOverridableFlags);
    }

    [Test]
    public void NonOverridableFlags_AreDeclaredFlagNames()
    {
        var constantNames = GetConstantFlagNames();

        FeatureFlagConstants.NonOverridableFlags.Should().BeSubsetOf(constantNames);
    }

    [Test]
    public void Catalog_HasNoDuplicateFlags()
    {
        var catalogNames = FeatureFlagCatalog.Descriptors.Select(descriptor => descriptor.FlagName).ToList();

        catalogNames.Should().OnlyHaveUniqueItems();
    }
}
