using System.Reflection;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Settings;
using Celbridge.Tests.Architecture;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Keeps FeatureFlagCatalog (the metadata behind the Features section of Project Settings) in sync with
/// FeatureFlagConstants (the canonical flag names).
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

    private static IReadOnlyList<FeatureFlagDescriptor> GetCatalogFlags()
    {
        return FeatureFlagCatalog.Groups
            .SelectMany(group => group.Flags)
            .ToList();
    }

    [Test]
    public void Catalog_CoversEveryKnownFeatureFlag()
    {
        var constantNames = GetConstantFlagNames();
        var catalogNames = GetCatalogFlags().Select(descriptor => descriptor.FlagName).ToList();

        catalogNames.Should().BeEquivalentTo(constantNames);
    }

    [Test]
    public void Catalog_HasNoDuplicateFlags()
    {
        var catalogNames = GetCatalogFlags().Select(descriptor => descriptor.FlagName).ToList();

        catalogNames.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void Catalog_HasNoEmptyGroups()
    {
        FeatureFlagCatalog.Groups.Should().OnlyContain(group => group.Flags.Count > 0, "an empty group draws a card with no rows");
    }

    [Test]
    public void Catalog_EveryResourceKeyExistsInTheResourceFile()
    {
        var keys = FeatureFlagCatalog.Groups
            .Select(group => group.TitleKey)
            .Concat(GetCatalogFlags().SelectMany(descriptor => new[] { descriptor.TitleKey, descriptor.DescriptionKey }))
            .ToList();

        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty("the tests locate the repository by walking up to Celbridge.slnx");

        var resourcePath = Path.Combine(sourceFolder, "Celbridge", "Resources", "Strings", "en-US", "Resources.resw");
        var resourceText = File.ReadAllText(resourcePath);

        foreach (var key in keys)
        {
            resourceText.Should().Contain($"name=\"{key}\"", $"the resource file must define '{key}', or the section shows the raw key");
        }
    }
}
