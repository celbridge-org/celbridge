using Celbridge.Dependencies;
using Celbridge.FileSystem.Services;
using Celbridge.LanguageServers.Helpers;
using Celbridge.LanguageServers.Services;
using Celbridge.Localization;
using Celbridge.Platform;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers the settings of the TypeScript server, and that the server asks for no file change reports.
/// </summary>
[TestFixture]
public class TypeScriptServerDefinitionTests
{
    [TestCase("typescript")]
    [TestCase("javascript")]
    public void CreateConfiguration_ALanguageSection_EnablesParameterNameAndEnumValueHints(string section)
    {
        var definition = CreateDefinition();

        var settings = definition.CreateConfiguration(section);

        settings.Should().NotBeNull();
        settings!["inlayHints"]!["parameterNames"]!["enabled"]!.GetValue<string>().Should().Be("literals");
        settings["inlayHints"]!["enumMemberValues"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
    }

    [Test]
    public void CreateConfiguration_AnotherSection_ReturnsNull()
    {
        var definition = CreateDefinition();

        var settings = definition.CreateConfiguration("editor");

        settings.Should().BeNull();
    }

    [Test]
    public void ReportedFiles_IsNull_BecauseTheCompilerWatchesTheProjectsFiles()
    {
        var definition = CreateDefinition();

        definition.ReportedFiles.Should().BeNull();
    }

    private static TypeScriptServerDefinition CreateDefinition()
    {
        var pin = new TypeScriptPinnedRelease("7.0.2", "https://example.com/registry", new List<TypeScriptPackage>());

        var dependency = new TypeScriptDependency(
            new LocalFileSystem(new NullLogger<LocalFileSystem>()),
            Substitute.For<IAppEnvironment>(),
            Substitute.For<IDependencyDownloader>(),
            Substitute.For<ILocalizerService>(),
            new NullLogger<TypeScriptDependency>(),
            pin);

        return new TypeScriptServerDefinition(dependency);
    }
}
