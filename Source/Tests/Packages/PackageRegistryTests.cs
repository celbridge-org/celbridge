using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class PackageServiceTests
{
    private string _tempProjectFolder = null!;
    private PackageService _service = null!;
    private IModuleService _moduleService = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageServiceTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var logger = Substitute.For<ILogger<PackageRegistry>>();
        var messengerService = Substitute.For<IMessengerService>();
        var localizationLogger = Substitute.For<ILogger<PackageLocalizationService>>();
        var localizationService = new PackageLocalizationService(localizationLogger);
        _moduleService = Substitute.For<IModuleService>();
        _moduleService.GetBundledPackageFolders().Returns(new List<string>());
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(Arg.Any<string>()).Returns(true);
        _service = new PackageService(logger, _moduleService, messengerService, featureFlags, localizationService);
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
    public void RegisterPackages_NoPackagesFolder_ReturnsEmpty()
    {
        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_EmptyPackagesFolder_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempProjectFolder, "packages"));

        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_ValidManifest_ReturnsManifest()
    {
        CreateProjectPackage("my-editor", "test.my-editor", "My Editor", "custom", ".myext");

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("My Editor");
        contributions[0].Should().BeOfType<CustomDocumentEditorContribution>();
    }

    [Test]
    public void RegisterPackages_MultiplePackages_ReturnsAll()
    {
        CreateProjectPackage("editor-a", "test.editor-a", "Editor A", "custom", ".a");
        CreateProjectPackage("editor-b", "test.editor-b", "Editor B", "code", ".b");

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Package.Name).ToList();
        names.Should().Contain("Editor A");
        names.Should().Contain("Editor B");
    }

    [Test]
    public void RegisterPackages_InvalidManifest_SkipsAndContinues()
    {
        CreateProjectPackage("good", "test.good", "Good", "custom", ".good");

        // Create an invalid package
        var badDir = Path.Combine(_tempProjectFolder, "packages", "bad");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "package.toml"), "{ invalid toml }");

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("Good");
    }

    [Test]
    public void RegisterPackages_FolderWithoutManifest_IsSkipped()
    {
        CreateProjectPackage("with-manifest", "test.with-manifest", "Found", "code", ".found");

        // Create a folder without a manifest
        var folderWithoutManifest = Path.Combine(_tempProjectFolder, "packages", "no-manifest");
        Directory.CreateDirectory(folderWithoutManifest);

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("Found");
    }

    [Test]
    public void RegisterPackages_IncludesModulePackages()
    {
        var bundledDir = CreateBundledPackage("bundled-editor", "test.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledPackageFolders().Returns(new List<string> { bundledDir });

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("Bundled");
    }

    [Test]
    public void RegisterPackages_CombinesProjectAndBundled()
    {
        CreateProjectPackage("proj-editor", "test.proj", "Project", "custom", ".proj");
        var bundledDir = CreateBundledPackage("bundled-editor", "test.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledPackageFolders().Returns(new List<string> { bundledDir });

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Package.Name).ToList();
        names.Should().Contain("Project");
        names.Should().Contain("Bundled");
    }

    [Test]
    public void RegisterPackages_InvalidBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "bad-bundled");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllText(Path.Combine(bundledDir, "package.toml"), "{ invalid toml }");

        _moduleService.GetBundledPackageFolders().Returns(new List<string> { bundledDir });

        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_MissingBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "no-manifest-bundled");
        Directory.CreateDirectory(bundledDir);

        _moduleService.GetBundledPackageFolders().Returns(new List<string> { bundledDir });

        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_ClearsPreviousContributions()
    {
        CreateProjectPackage("editor-a", "test.editor-a", "Editor A", "custom", ".a");

        _service.RegisterPackages(_tempProjectFolder);
        _service.GetAllDocumentEditors().Should().HaveCount(1);

        // Create a second temp folder with different packages
        var secondFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageServiceTests) + "_2");
        try
        {
            Directory.CreateDirectory(secondFolder);

            // Discover from empty folder - should clear previous contributions
            _service.RegisterPackages(secondFolder);
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
    /// Creates a package in the project's packages/ folder with a standard structure.
    /// </summary>
    private string CreateProjectPackage(string dirName, string packageId, string packageName, string docType, string fileExt)
    {
        var packageDir = Path.Combine(_tempProjectFolder, "packages", dirName);
        Directory.CreateDirectory(packageDir);
        WritePackageFiles(packageDir, packageId, packageName, docType, fileExt);
        return packageDir;
    }

    /// <summary>
    /// Creates a bundled package in a standalone folder.
    /// </summary>
    private string CreateBundledPackage(string dirName, string packageId, string packageName, string docType, string fileExt)
    {
        var packageDir = Path.Combine(_tempProjectFolder, dirName);
        Directory.CreateDirectory(packageDir);
        WritePackageFiles(packageDir, packageId, packageName, docType, fileExt);
        return packageDir;
    }

    private static void WritePackageFiles(string packageDir, string packageId, string packageName, string docType, string fileExt)
    {
        File.WriteAllText(Path.Combine(packageDir, "package.toml"), $"""
            [package]
            id = "{packageId}"
            name = "{packageName}"
            version = "1.0.0"

            [contributes]
            document_editors = ["editor.document.toml"]
            """);

        File.WriteAllText(Path.Combine(packageDir, "editor.document.toml"), $"""
            [document]
            id = "{packageId}-doc"
            type = "{docType}"

            [[document_file_types]]
            extension = "{fileExt}"
            """);
    }
}
