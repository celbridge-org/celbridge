using Celbridge.Dependencies;
using Celbridge.FileSystem.Services;
using Celbridge.LanguageServers.Helpers;
using Celbridge.LanguageServers.Services;
using Celbridge.Localization;
using Celbridge.Platform;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers that the TypeScript server runs with its default settings, and asks for no file change reports.
/// </summary>
[TestFixture]
public class TypeScriptServerDefinitionTests
{
    [TestCase("typescript")]
    [TestCase("javascript")]
    [TestCase("editor")]
    public void CreateConfiguration_AnySection_ReturnsNull(string section)
    {
        var definition = CreateDefinition();

        var settings = definition.CreateConfiguration(section);

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
