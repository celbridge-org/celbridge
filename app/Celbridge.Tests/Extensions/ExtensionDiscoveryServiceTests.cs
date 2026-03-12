using Celbridge.Documents.Extensions;
using Celbridge.Logging;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class ExtensionDiscoveryServiceTests
{
    private string _tempProjectFolder = null!;
    private ExtensionDiscoveryService _service = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ExtensionDiscoveryServiceTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var logger = Substitute.For<ILogger<ExtensionDiscoveryService>>();
        _service = new ExtensionDiscoveryService(logger);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempProjectFolder))
        {
            Directory.Delete(_tempProjectFolder, true);
        }
    }

    [Test]
    public void DiscoverExtensions_NoExtensionsFolder_ReturnsEmpty()
    {
        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().BeEmpty();
    }

    [Test]
    public void DiscoverExtensions_EmptyExtensionsFolder_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempProjectFolder, "extensions"));

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().BeEmpty();
    }

    [Test]
    public void DiscoverExtensions_ValidManifest_ReturnsManifest()
    {
        var extDir = Path.Combine(_tempProjectFolder, "extensions", "my-editor");
        Directory.CreateDirectory(extDir);

        var json = """
            {
                "name": "My Editor",
                "type": "custom",
                "extensions": [".myext"],
                "entryPoint": "index.html"
            }
            """;
        File.WriteAllText(Path.Combine(extDir, "editor.json"), json);

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be("My Editor");
        manifests[0].Type.Should().Be(ExtensionEditorType.Custom);
    }

    [Test]
    public void DiscoverExtensions_MultipleExtensions_ReturnsAll()
    {
        var ext1Dir = Path.Combine(_tempProjectFolder, "extensions", "editor-a");
        var ext2Dir = Path.Combine(_tempProjectFolder, "extensions", "editor-b");
        Directory.CreateDirectory(ext1Dir);
        Directory.CreateDirectory(ext2Dir);

        File.WriteAllText(Path.Combine(ext1Dir, "editor.json"), """
            { "name": "Editor A", "type": "custom", "extensions": [".a"] }
            """);
        File.WriteAllText(Path.Combine(ext2Dir, "editor.json"), """
            { "name": "Editor B", "type": "code", "extensions": [".b"] }
            """);

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(2);
        var names = manifests.Select(m => m.Name).ToList();
        names.Should().Contain("Editor A");
        names.Should().Contain("Editor B");
    }

    [Test]
    public void DiscoverExtensions_InvalidManifest_SkipsAndContinues()
    {
        var goodDir = Path.Combine(_tempProjectFolder, "extensions", "good");
        var badDir = Path.Combine(_tempProjectFolder, "extensions", "bad");
        Directory.CreateDirectory(goodDir);
        Directory.CreateDirectory(badDir);

        File.WriteAllText(Path.Combine(goodDir, "editor.json"), """
            { "name": "Good", "type": "custom", "extensions": [".good"] }
            """);
        File.WriteAllText(Path.Combine(badDir, "editor.json"), "{ invalid json }");

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be("Good");
    }

    [Test]
    public void DiscoverExtensions_DirectoryWithoutManifest_IsSkipped()
    {
        var dirWithManifest = Path.Combine(_tempProjectFolder, "extensions", "with-manifest");
        var dirWithoutManifest = Path.Combine(_tempProjectFolder, "extensions", "no-manifest");
        Directory.CreateDirectory(dirWithManifest);
        Directory.CreateDirectory(dirWithoutManifest);

        File.WriteAllText(Path.Combine(dirWithManifest, "editor.json"), """
            { "name": "Found", "type": "code", "extensions": [".found"] }
            """);
        // dirWithoutManifest has no editor.json

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be("Found");
    }
}
