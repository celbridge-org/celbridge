using System.Reflection;
using System.Text.Json;
using Celbridge.Settings;
using Celbridge.Tests.Architecture;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Keeps the FeatureFlags sections of appsettings.json and the local appsettings.development.json in step
/// with FeatureFlagConstants.
/// </summary>
[TestFixture]
public class FeatureFlagDefaultsTests
{
    private const string FeatureFlagsSectionName = "FeatureFlags";
    private const string AppSettingsFileName = "appsettings.json";
    private const string DevelopmentAppSettingsFileName = "appsettings.development.json";

    [Test]
    public void AppSettings_ListsEveryDeclaredFlag()
    {
        var declaredNames = GetDeclaredFlagNames();
        var settingsPath = GetSettingsPath(AppSettingsFileName);
        var configuredNames = ReadConfiguredFlags(settingsPath).Keys;

        configuredNames.Should().Contain(declaredNames, "every declared flag needs an explicit default");
    }

    [Test]
    public void AppSettings_ListsOnlyDeclaredFlags()
    {
        var declaredNames = GetDeclaredFlagNames();
        var settingsPath = GetSettingsPath(AppSettingsFileName);
        var configuredNames = ReadConfiguredFlags(settingsPath).Keys;

        configuredNames.Should().BeSubsetOf(declaredNames, "an entry naming no declared flag is never read");
    }

    [Test]
    public void AppSettings_FlagValuesAreBooleans()
    {
        var settingsPath = GetSettingsPath(AppSettingsFileName);
        var configuredFlags = ReadConfiguredFlags(settingsPath);
        var nonBooleanNames = GetNonBooleanFlagNames(configuredFlags);

        nonBooleanNames.Should().BeEmpty("a value that is not a boolean resolves to off");
    }

    [Test]
    public void DevelopmentAppSettings_ListsOnlyDeclaredFlags()
    {
        var settingsPath = GetSettingsPath(DevelopmentAppSettingsFileName);
        if (!File.Exists(settingsPath))
        {
            Assert.Ignore("This machine has no local appsettings.development.json to check.");
        }

        var declaredNames = GetDeclaredFlagNames();
        var configuredNames = ReadConfiguredFlags(settingsPath).Keys;

        configuredNames.Should().BeSubsetOf(declaredNames, "an entry naming no declared flag is never read");
    }

    [Test]
    public void DevelopmentAppSettings_FlagValuesAreBooleans()
    {
        var settingsPath = GetSettingsPath(DevelopmentAppSettingsFileName);
        if (!File.Exists(settingsPath))
        {
            Assert.Ignore("This machine has no local appsettings.development.json to check.");
        }

        var configuredFlags = ReadConfiguredFlags(settingsPath);
        var nonBooleanNames = GetNonBooleanFlagNames(configuredFlags);

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

    private static string GetSettingsPath(string settingsFileName)
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty("the tests locate the repository by walking up to Celbridge.slnx");

        return Path.Combine(sourceFolder, "Celbridge", settingsFileName);
    }

    // Maps each entry in the FeatureFlags section of a settings file to the JSON kind of its value.
    private static IReadOnlyDictionary<string, JsonValueKind> ReadConfiguredFlags(string settingsPath)
    {
        var settingsJson = File.ReadAllText(settingsPath);
        using var document = JsonDocument.Parse(settingsJson);

        var configuredFlags = new Dictionary<string, JsonValueKind>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty(FeatureFlagsSectionName, out var flagsElement))
        {
            foreach (var property in flagsElement.EnumerateObject())
            {
                configuredFlags[property.Name] = property.Value.ValueKind;
            }
        }

        return configuredFlags;
    }

    private static IReadOnlyList<string> GetNonBooleanFlagNames(IReadOnlyDictionary<string, JsonValueKind> configuredFlags)
    {
        return configuredFlags
            .Where(entry => entry.Value is not (JsonValueKind.True or JsonValueKind.False))
            .Select(entry => entry.Key)
            .ToList();
    }
}
