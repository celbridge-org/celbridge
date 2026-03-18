using Celbridge.Extensions;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Settings;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class ExtensionServiceTests
{
    private string _tempProjectFolder = null!;
    private ExtensionService _service = null!;
    private IModuleService _moduleService = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ExtensionServiceTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var logger = Substitute.For<ILogger<ExtensionRegistry>>();
        var messengerService = Substitute.For<IMessengerService>();
        var localizationLogger = Substitute.For<ILogger<ExtensionLocalizationService>>();
        var localizationService = new ExtensionLocalizationService(localizationLogger);
        _moduleService = Substitute.For<IModuleService>();
        _moduleService.GetBundledExtensionFolders().Returns(new List<string>());
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(Arg.Any<string>()).Returns(true);
        _service = new ExtensionService(logger, _moduleService, messengerService, featureFlags, localizationService);
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
    public void RegisterExtensions_NoExtensionsFolder_ReturnsEmpty()
    {
        _service.RegisterExtensions(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterExtensions_EmptyExtensionsFolder_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempProjectFolder, "extensions"));

        _service.RegisterExtensions(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterExtensions_ValidManifest_ReturnsManifest()
    {
        CreateProjectExtension("my-editor", "test.my-editor", "My Editor", "custom", ".myext");

        _service.RegisterExtensions(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Extension.Name.Should().Be("My Editor");
        contributions[0].Should().BeOfType<CustomDocumentContribution>();
    }

    [Test]
    public void RegisterExtensions_MultipleExtensions_ReturnsAll()
    {
        CreateProjectExtension("editor-a", "test.editor-a", "Editor A", "custom", ".a");
        CreateProjectExtension("editor-b", "test.editor-b", "Editor B", "code", ".b");

        _service.RegisterExtensions(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Extension.Name).ToList();
        names.Should().Contain("Editor A");
        names.Should().Contain("Editor B");
    }

    [Test]
    public void RegisterExtensions_InvalidManifest_SkipsAndContinues()
    {
        CreateProjectExtension("good", "test.good", "Good", "custom", ".good");

        // Create an invalid extension
        var badDir = Path.Combine(_tempProjectFolder, "extensions", "bad");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "extension.toml"), "{ invalid toml }");

        _service.RegisterExtensions(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Extension.Name.Should().Be("Good");
    }

    [Test]
    public void RegisterExtensions_DirectoryWithoutManifest_IsSkipped()
    {
        CreateProjectExtension("with-manifest", "test.with-manifest", "Found", "code", ".found");

        // Create a directory without a manifest
        var dirWithoutManifest = Path.Combine(_tempProjectFolder, "extensions", "no-manifest");
        Directory.CreateDirectory(dirWithoutManifest);

        _service.RegisterExtensions(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Extension.Name.Should().Be("Found");
    }

    [Test]
    public void RegisterExtensions_IncludesModuleExtensions()
    {
        var bundledDir = CreateBundledExtension("bundled-editor", "test.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledExtensionFolders().Returns(new List<string> { bundledDir });

        _service.RegisterExtensions(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Extension.Name.Should().Be("Bundled");
    }

    [Test]
    public void RegisterExtensions_CombinesProjectAndBundled()
    {
        CreateProjectExtension("proj-editor", "test.proj", "Project", "custom", ".proj");
        var bundledDir = CreateBundledExtension("bundled-editor", "test.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledExtensionFolders().Returns(new List<string> { bundledDir });

        _service.RegisterExtensions(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Extension.Name).ToList();
        names.Should().Contain("Project");
        names.Should().Contain("Bundled");
    }

    [Test]
    public void RegisterExtensions_InvalidBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "bad-bundled");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllText(Path.Combine(bundledDir, "extension.toml"), "{ invalid toml }");

        _moduleService.GetBundledExtensionFolders().Returns(new List<string> { bundledDir });

        _service.RegisterExtensions(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterExtensions_MissingBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "no-manifest-bundled");
        Directory.CreateDirectory(bundledDir);

        _moduleService.GetBundledExtensionFolders().Returns(new List<string> { bundledDir });

        _service.RegisterExtensions(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterExtensions_ClearsPreviousContributions()
    {
        CreateProjectExtension("editor-a", "test.editor-a", "Editor A", "custom", ".a");

        _service.RegisterExtensions(_tempProjectFolder);
        _service.GetAllDocumentEditors().Should().HaveCount(1);

        // Create a second temp folder with different extensions
        var secondFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ExtensionServiceTests) + "_2");
        try
        {
            Directory.CreateDirectory(secondFolder);

            // Discover from empty folder - should clear previous contributions
            _service.RegisterExtensions(secondFolder);
            _service.GetAllDocumentEditors().Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(secondFolder))
            {
                Directory.Delete(secondFolder, true);
            }
        }
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
            document_editors = ["editor.document.toml"]
            """);

        File.WriteAllText(Path.Combine(extDir, "editor.document.toml"), $"""
            [document]
            id = "{extId}-doc"
            type = "{docType}"

            [[document_file_types]]
            extension = "{fileExt}"
            """);
    }
}
