using Celbridge.Extensions;
using Celbridge.Logging;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class ExtensionRegistryTests
{
    private string _tempProjectFolder = null!;
    private ExtensionRegistry _service = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ExtensionRegistryTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var logger = Substitute.For<ILogger<ExtensionRegistry>>();
        _service = new ExtensionRegistry(logger);
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
        var extDir = CreateProjectExtension("my-editor", "test.my-editor", "My Editor", "custom", ".myext");

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be("My Editor");
        manifests[0].Type.Should().Be(EditorType.Custom);
    }

    [Test]
    public void DiscoverExtensions_MultipleExtensions_ReturnsAll()
    {
        CreateProjectExtension("editor-a", "test.editor-a", "Editor A", "custom", ".a");
        CreateProjectExtension("editor-b", "test.editor-b", "Editor B", "code", ".b");

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(2);
        var names = manifests.Select(m => m.Name).ToList();
        names.Should().Contain("Editor A");
        names.Should().Contain("Editor B");
    }

    [Test]
    public void DiscoverExtensions_InvalidManifest_SkipsAndContinues()
    {
        CreateProjectExtension("good", "test.good", "Good", "custom", ".good");

        // Create an invalid extension
        var badDir = Path.Combine(_tempProjectFolder, "extensions", "bad");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "extension.toml"), "{ invalid toml }");

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be("Good");
    }

    [Test]
    public void DiscoverExtensions_DirectoryWithoutManifest_IsSkipped()
    {
        CreateProjectExtension("with-manifest", "test.with-manifest", "Found", "code", ".found");

        // Create a directory without a manifest
        var dirWithoutManifest = Path.Combine(_tempProjectFolder, "extensions", "no-manifest");
        Directory.CreateDirectory(dirWithoutManifest);

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be("Found");
    }

    [Test]
    public void RegisterBundledExtensionPath_DiscoverIncludesBundled()
    {
        var bundledDir = CreateBundledExtension("bundled-editor", "test.bundled", "Bundled", "custom", ".bnd");

        _service.RegisterBundledExtensionPath(bundledDir);

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(1);
        manifests[0].Name.Should().Be("Bundled");
    }

    [Test]
    public void RegisterBundledExtensionPath_CombinesProjectAndBundled()
    {
        CreateProjectExtension("proj-editor", "test.proj", "Project", "custom", ".proj");
        var bundledDir = CreateBundledExtension("bundled-editor", "test.bundled", "Bundled", "custom", ".bnd");

        _service.RegisterBundledExtensionPath(bundledDir);

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().HaveCount(2);
        var names = manifests.Select(m => m.Name).ToList();
        names.Should().Contain("Project");
        names.Should().Contain("Bundled");
    }

    [Test]
    public void RegisterBundledExtensionPath_DuplicatePathIgnored()
    {
        var bundledDir = CreateBundledExtension("bundled-editor", "test.bundled", "Bundled", "custom", ".bnd");

        _service.RegisterBundledExtensionPath(bundledDir);
        _service.RegisterBundledExtensionPath(bundledDir);

        _service.BundledExtensionPaths.Should().HaveCount(1);
    }

    [Test]
    public void RegisterBundledExtensionPath_InvalidManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "bad-bundled");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllText(Path.Combine(bundledDir, "extension.toml"), "{ invalid toml }");

        _service.RegisterBundledExtensionPath(bundledDir);

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().BeEmpty();
    }

    [Test]
    public void RegisterBundledExtensionPath_MissingManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "no-manifest-bundled");
        Directory.CreateDirectory(bundledDir);

        _service.RegisterBundledExtensionPath(bundledDir);

        var manifests = _service.DiscoverExtensions(_tempProjectFolder);

        manifests.Should().BeEmpty();
    }

    /// <summary>
    /// Creates an extension in the project's extensions/ directory with a standard structure.
    /// </summary>
    private string CreateProjectExtension(string dirName, string extId, string extName, string docType, string fileExt)
    {
        var extDir = Path.Combine(_tempProjectFolder, "extensions", dirName);
        Directory.CreateDirectory(extDir);
        WriteExtensionFiles(extDir, extId, extName, docType, fileExt);
        return extDir;
    }

    /// <summary>
    /// Creates a bundled extension in a standalone directory.
    /// </summary>
    private string CreateBundledExtension(string dirName, string extId, string extName, string docType, string fileExt)
    {
        var extDir = Path.Combine(_tempProjectFolder, dirName);
        Directory.CreateDirectory(extDir);
        WriteExtensionFiles(extDir, extId, extName, docType, fileExt);
        return extDir;
    }

    private static void WriteExtensionFiles(string extDir, string extId, string extName, string docType, string fileExt)
    {
        File.WriteAllText(Path.Combine(extDir, "extension.toml"), $"""
            [extension]
            id = "{extId}"
            name = "{extName}"
            version = "1.0.0"

            [contributes]
            documents = ["editor.document.toml"]
            """);

        File.WriteAllText(Path.Combine(extDir, "editor.document.toml"), $"""
            [document]
            id = "{extId}-doc"
            type = "{docType}"

            [[file_types]]
            extension = "{fileExt}"
            """);
    }
}
