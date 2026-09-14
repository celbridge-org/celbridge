using System.Reflection;
using System.Text.Json;
using Celbridge.Settings;
using Celbridge.Tests.Architecture;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Keeps the FeatureFlags section of appsettings.json in step with FeatureFlagConstants.
/// </summary>
[TestFixture]
public class FeatureFlagDefaultsTests
{
    private const string FeatureFlagsSectionName = "FeatureFlags";

    [Test]
    public void AppSettings_ListsEveryDeclaredFlag()
    {
        var declaredNames = GetDeclaredFlagNames();
        var configuredNames = ReadConfiguredFlags().Keys;

        configuredNames.Should().Contain(declaredNames, "every declared flag needs an explicit default");
    }

    [Test]
    public void AppSettings_ListsOnlyDeclaredFlags()
    {
        var declaredNames = GetDeclaredFlagNames();
        var configuredNames = ReadConfiguredFlags().Keys;

        configuredNames.Should().BeSubsetOf(declaredNames, "an entry naming no declared flag is never read");
    }

    [Test]
    public void AppSettings_FlagValuesAreBooleans()
    {
        var configuredFlags = ReadConfiguredFlags();

        var nonBooleanNames = configuredFlags
            .Where(entry => entry.Value is not (JsonValueKind.True or JsonValueKind.False))
            .Select(entry => entry.Key)
            .ToList();

        nonBooleanNames.Should().BeEmpty("a value that is not a boolean resolves to off");
    }

    private static IReadOnlyList<string> GetDeclaredFlagNames()
    {
        return typeof(FeatureFlagConstants)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
    }

    // Maps each entry in the FeatureFlags section of appsettings.json to the JSON kind of its value.
    private static IReadOnlyDictionary<string, JsonValueKind> ReadConfiguredFlags()
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty("the tests locate the repository by walking up to Celbridge.slnx");

        var settingsPath = Path.Combine(sourceFolder, "Celbridge", "appsettings.json");
        var settingsJson = File.ReadAllText(settingsPath);

        using var document = JsonDocument.Parse(settingsJson);
        var flagsElement = document.RootElement.GetProperty(FeatureFlagsSectionName);

        var configuredFlags = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal);
        foreach (var property in flagsElement.EnumerateObject())
        {
            configuredFlags[property.Name] = property.Value.ValueKind;
        }

        return configuredFlags;
    }
}
