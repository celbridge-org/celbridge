using Celbridge.Console;
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
    private IMessengerService _messengerService = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageServiceTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var logger = Substitute.For<ILogger<PackageRegistry>>();
        _messengerService = Substitute.For<IMessengerService>();
        var localizationLogger = Substitute.For<ILogger<PackageLocalizationService>>();
        var localizationService = new PackageLocalizationService(localizationLogger);
        _moduleService = Substitute.For<IModuleService>();
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor>());
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(Arg.Any<string>()).Returns(true);
        _service = new PackageService(logger, _moduleService, _messengerService, featureFlags, localizationService);
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
        CreateProjectPackage("my-editor", "my-editor", "My Editor", "custom", ".myext");

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("My Editor");
        contributions[0].Should().BeOfType<CustomDocumentEditorContribution>();
    }

    [Test]
    public void RegisterPackages_MultiplePackages_ReturnsAll()
    {
        CreateProjectPackage("editor-a", "editor-a", "Editor A", "custom", ".a");
        CreateProjectPackage("editor-b", "editor-b", "Editor B", "code", ".b");

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
        CreateProjectPackage("good", "good", "Good", "custom", ".good");

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
        CreateProjectPackage("with-manifest", "with-manifest", "Found", "code", ".found");

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
        var bundledDir = CreateBundledPackage("bundled-editor", "celbridge.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("Bundled");
    }

    [Test]
    public void RegisterPackages_CombinesProjectAndBundled()
    {
        CreateProjectPackage("proj-editor", "proj", "Project", "custom", ".proj");
        var bundledDir = CreateBundledPackage("bundled-editor", "celbridge.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Package.Name).ToList();
        names.Should().Contain("Project");
        names.Should().Contain("Bundled");
    }

    [Test]
    public void RegisterPackages_ProjectPackageWithReservedIdPrefix_Skipped()
    {
        // Project packages may not claim an id under the reserved "celbridge." namespace.
        CreateProjectPackage("impostor", "celbridge.notes", "Impostor Notes", "custom", ".imp");
        CreateProjectPackage("legit", "legit", "Legit", "custom", ".legit");

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Id.Should().Be("legit");
    }

    [Test]
    public void RegisterPackages_ProjectPackageWithMixedCaseId_RejectedByFormatValidation()
    {
        // Package ids are lowercase-only. A mixed-case id fails manifest validation
        // before the reserved-prefix check runs, so "Celbridge.Something" cannot be
        // used as a workaround for the prefix block.
        CreateProjectPackage("mixed-case", "Celbridge.Something", "Mixed Case", "custom", ".mc");

        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_ProjectPackageWithDottedId_Skipped()
    {
        // Until a namespace registry exists, project packages cannot claim a
        // dotted id because there is no way to validate namespace ownership.
        // Only flat global-namespace ids are permitted for project packages.
        CreateProjectPackage("dotted", "acme.tool", "Dotted", "custom", ".dot");
        CreateProjectPackage("flat", "legit-tool", "Flat", "custom", ".flat");

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Id.Should().Be("legit-tool");
    }

    [Test]
    public void RegisterPackages_BundledPackageWithReservedIdPrefix_Allowed()
    {
        // Bundled packages are the intended owners of the "celbridge." namespace.
        var bundledDir = CreateBundledPackage("bundled-official", "celbridge.notes", "Official Notes", "custom", ".note");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Id.Should().Be("celbridge.notes");
    }

    [Test]
    public void RegisterPackages_TwoBundledPackagesSameId_BothSkipped()
    {
        // Two bundled packages with the same id is a first-party build bug.
        // Both are skipped rather than silently picking a winner.
        var dirA = CreateBundledPackage("bundled-a", "celbridge.conflict", "Conflict A", "custom", ".a");
        var dirB = CreateBundledPackage("bundled-b", "celbridge.conflict", "Conflict B", "custom", ".b");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor>
        {
            new() { Folder = dirA },
            new() { Folder = dirB }
        });

        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_ProjectPackageConflictsWithBundled_ProjectSkipped()
    {
        // Bundled wins over project when ids collide. Both use flat ids here
        // so the collision check is what rejects the project package, not the
        // reserved-prefix or unregistered-namespace rules.
        var bundledDir = CreateBundledPackage("bundled", "shared-id", "Bundled", "custom", ".bnd");
        CreateProjectPackage("project", "shared-id", "Project", "custom", ".prj");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("Bundled");
    }

    [Test]
    public void RegisterPackages_TwoProjectPackagesSameId_BothSkipped()
    {
        // Two project packages with the same id cannot be distinguished so
        // both are skipped. A non-colliding sibling continues to load.
        CreateProjectPackage("dup-a", "dup-tool", "Dup A", "custom", ".a");
        CreateProjectPackage("dup-b", "dup-tool", "Dup B", "custom", ".b");
        CreateProjectPackage("legit", "other-tool", "Legit", "custom", ".legit");

        _service.RegisterPackages(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Id.Should().Be("other-tool");
    }

    [Test]
    public void RegisterPackages_LoadFailures_SendPackageLoadErrorMessage()
    {
        CreateProjectPackage("dup-a", "dup-tool", "Dup A", "custom", ".a");
        CreateProjectPackage("dup-b", "dup-tool", "Dup B", "custom", ".b");

        _service.RegisterPackages(_tempProjectFolder);

        _messengerService.Received(1).Send(Arg.Is<ConsoleErrorMessage>(m => m.ErrorType == ConsoleErrorType.PackageLoadError));
    }

    [Test]
    public void RegisterPackages_NoFailures_DoesNotSendPackageLoadErrorMessage()
    {
        CreateProjectPackage("legit", "legit", "Legit", "custom", ".legit");

        _service.RegisterPackages(_tempProjectFolder);

        _messengerService.DidNotReceive().Send(Arg.Is<ConsoleErrorMessage>(m => m.ErrorType == ConsoleErrorType.PackageLoadError));
    }

    [Test]
    public void RegisterPackages_InvalidBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "bad-bundled");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllText(Path.Combine(bundledDir, "package.toml"), "{ invalid toml }");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_MissingBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "no-manifest-bundled");
        Directory.CreateDirectory(bundledDir);

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        _service.RegisterPackages(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public void RegisterPackages_ClearsPreviousContributions()
    {
        CreateProjectPackage("editor-a", "editor-a", "Editor A", "custom", ".a");

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
            display_name = "TestEditor"

            [[document_file_types]]
            extension = "{fileExt}"
            display_name = "TestFileType"
            """);
    }
}
