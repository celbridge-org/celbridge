using Celbridge.Settings;
using Celbridge.Tests.Architecture;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Keeps the groups of FeatureFlagCatalog in step with the flags the catalog declares. The Features section of
/// Project Settings shows the groups.
/// </summary>
[TestFixture]
public class FeatureFlagCatalogTests
{
    private static IReadOnlyList<FeatureFlagDescriptor> GetCatalogFlags()
    {
        return FeatureFlagCatalog.Groups
            .SelectMany(group => group.Flags)
            .ToList();
    }

    [Test]
    public void Groups_ListEveryDeclaredFlag()
    {
        var declaredFlags = DeclaredFeatureFlags.Read();

        GetCatalogFlags().Should().BeEquivalentTo(declaredFlags);
    }

    [Test]
    public void Catalog_HasNoDuplicateFlags()
    {
        var catalogNames = GetCatalogFlags().Select(descriptor => descriptor.Name).ToList();
        var declaredNames = DeclaredFeatureFlags.Read().Select(descriptor => descriptor.Name).ToList();

        catalogNames.Should().OnlyHaveUniqueItems();
        declaredNames.Should().OnlyHaveUniqueItems("two descriptors with one name would share one project override");
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
