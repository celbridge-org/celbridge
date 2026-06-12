using Celbridge.Console;
using Celbridge.FileSystem.Services;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Settings;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class PackageServiceTests
{
    private string _tempProjectFolder = null!;
    private PackageService _service = null!;
    private IModuleService _moduleService = null!;
    private IMessengerService _messengerService = null!;
    private IProjectLoadReporter _loadReporter = null!;
    private IResourceRegistry _resourceRegistry = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageServiceTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var logger = Substitute.For<ILogger<PackageRegistry>>();
        _messengerService = Substitute.For<IMessengerService>();
        _moduleService = Substitute.For<IModuleService>();
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor>());
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(Arg.Any<string>()).Returns(true);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempProjectFolder);
        _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var key = callInfo.Arg<ResourceKey>();
            return Result<string>.Ok(Path.Combine(_tempProjectFolder, key.Path.Replace('/', Path.DirectorySeparatorChar)));
        });
        _resourceRegistry.GetResourceKey(Arg.Any<string>()).Returns(callInfo =>
        {
            var path = callInfo.Arg<string>();
            if (!path.StartsWith(_tempProjectFolder, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ResourceKey>.Fail($"Path '{path}' is not under the project root");
            }
            var relative = Path.GetRelativePath(_tempProjectFolder, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            return Result<ResourceKey>.Ok(new ResourceKey(relative));
        });

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        // Mirror the real SidecarService trailing-extension rule so package
        // discovery rejects any document-file-type extension that lands in
        // the reserved .cel namespace.
        var sidecarService = Substitute.For<ISidecarService>();
        sidecarService.IsSidecarFileName(Arg.Any<string>()).Returns(callInfo =>
        {
            var input = callInfo.Arg<string>();
            return !string.IsNullOrEmpty(input)
                && input.EndsWith(".cel", StringComparison.OrdinalIgnoreCase);
        });
        resourceService.Sidecars.Returns(sidecarService);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        var fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());

        var localizationLogger = Substitute.For<ILogger<PackageLocalizationService>>();
        var localizationService = new PackageLocalizationService(localizationLogger, workspaceWrapper, fileSystem);

        var registry = new PackageRegistry(logger, _moduleService, featureFlags, localizationService, workspaceWrapper, fileSystem);
        _loadReporter = Substitute.For<IProjectLoadReporter>();
        _service = new PackageService(_messengerService, _loadReporter, registry);
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
    public async Task RegisterPackages_NoPackagesFolder_ReturnsEmpty()
    {
        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_EmptyPackagesFolder_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempProjectFolder, "packages"));

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ValidManifest_ReturnsManifest()
    {
        CreateProjectPackage("my-editor", "my-editor", "My Editor", "custom", ".myext");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("My Editor");
        contributions[0].Should().BeOfType<CustomDocumentEditorContribution>();
    }

    [Test]
    public async Task RegisterPackages_MultiplePackages_ReturnsAll()
    {
        CreateProjectPackage("editor-a", "editor-a", "Editor A", "custom", ".a");
        CreateProjectPackage("editor-b", "editor-b", "Editor B", "code", ".b");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Package.Title).ToList();
        names.Should().Contain("Editor A");
        names.Should().Contain("Editor B");
    }

    [Test]
    public async Task RegisterPackages_InvalidManifest_SkipsAndContinues()
    {
        CreateProjectPackage("good", "good", "Good", "custom", ".good");

        // Create an invalid package
        var badDir = Path.Combine(_tempProjectFolder, "packages", "bad");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "package.toml"), "{ invalid toml }");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Good");
    }

    [Test]
    public async Task RegisterPackages_FolderWithoutManifest_IsSkipped()
    {
        CreateProjectPackage("with-manifest", "with-manifest", "Found", "code", ".found");

        // Create a folder without a manifest
        var folderWithoutManifest = Path.Combine(_tempProjectFolder, "packages", "no-manifest");
        Directory.CreateDirectory(folderWithoutManifest);

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Found");
    }

    [Test]
    public async Task RegisterPackages_IncludesModulePackages()
    {
        var bundledDir = CreateBundledPackage("bundled-editor", "celbridge.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Bundled");
    }

    [Test]
    public async Task RegisterPackages_CombinesProjectAndBundled()
    {
        CreateProjectPackage("proj-editor", "proj", "Project", "custom", ".proj");
        var bundledDir = CreateBundledPackage("bundled-editor", "celbridge.bundled", "Bundled", "custom", ".bnd");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Package.Title).ToList();
        names.Should().Contain("Project");
        names.Should().Contain("Bundled");
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageWithReservedNamePrefix_Skipped()
    {
        // Project packages may not claim a name under the reserved "celbridge." namespace.
        CreateProjectPackage("impostor", "celbridge.notes", "Impostor Notes", "custom", ".imp");
        CreateProjectPackage("legit", "legit", "Legit", "custom", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("legit");
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageWithMixedCaseName_RejectedByFormatValidation()
    {
        // Package names are lowercase-only. A mixed-case name fails manifest validation
        // before the reserved-prefix check runs, so "Celbridge.Something" cannot be
        // used as a workaround for the prefix block.
        CreateProjectPackage("mixed-case", "Celbridge.Something", "Mixed Case", "custom", ".mc");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageWithDottedName_Skipped()
    {
        // Until a namespace registry exists, project packages cannot claim a
        // dotted name because there is no way to validate namespace ownership.
        // Only flat global-namespace names are permitted for project packages.
        CreateProjectPackage("dotted", "acme.tool", "Dotted", "custom", ".dot");
        CreateProjectPackage("flat", "legit-tool", "Flat", "custom", ".flat");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("legit-tool");
    }

    [TestCase(".cel")]
    [TestCase(".foo.cel")]
    public async Task RegisterPackages_ProjectPackageWithCelExtension_Skipped(string reservedExtension)
    {
        // Document types cannot register inside the reserved .cel namespace.
        // The check rejects both the bare suffix and multi-part forms that end
        // in .cel; the classifier would otherwise pre-empt the editor.
        CreateProjectPackage("reserved", "reserved-ext", "Reserved", "custom", reservedExtension);
        CreateProjectPackage("legit", "legit", "Legit", "custom", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("legit");
    }

    [Test]
    public async Task RegisterPackages_PackageWithCelInMiddleSegment_Accepted()
    {
        // Only the trailing extension is reserved. .cel.bar ends in .bar and
        // does not conflict with sidecar pairing — the editor binds normally
        // and the sidecar lives next to the parent at <name>.cel.bar.cel.
        CreateProjectPackage("midcel", "mid-cel", "MidCel", "custom", ".cel.bar");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("mid-cel");
    }

    [Test]
    public async Task RegisterPackages_BundledPackageWithCelExtension_Skipped()
    {
        // The reservation applies to bundled and project packages alike —
        // first-party code can't register in the .cel namespace either.
        var reservedDir = CreateBundledPackage("bundled-reserved", "celbridge.reserved", "Bundled Reserved", "custom", ".cel");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = reservedDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_BundledPackageWithReservedNamePrefix_Allowed()
    {
        // Bundled packages are the intended owners of the "celbridge." namespace.
        var bundledDir = CreateBundledPackage("bundled-official", "celbridge.notes", "Official Notes", "custom", ".note");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("celbridge.notes");
    }

    [Test]
    public async Task RegisterPackages_TwoBundledPackagesSameName_BothSkipped()
    {
        // Two bundled packages with the same name is a first-party build bug.
        // Both are skipped rather than silently picking a winner.
        var dirA = CreateBundledPackage("bundled-a", "celbridge.conflict", "Conflict A", "custom", ".a");
        var dirB = CreateBundledPackage("bundled-b", "celbridge.conflict", "Conflict B", "custom", ".b");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor>
        {
            new() { Folder = dirA },
            new() { Folder = dirB }
        });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageConflictsWithBundled_ProjectSkipped()
    {
        // Bundled wins over project when names collide. Both use flat names here
        // so the collision check is what rejects the project package, not the
        // reserved-prefix or unregistered-namespace rules.
        var bundledDir = CreateBundledPackage("bundled", "shared-id", "Bundled", "custom", ".bnd");
        CreateProjectPackage("project", "shared-id", "Project", "custom", ".prj");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Bundled");
    }

    [Test]
    public async Task RegisterPackages_TwoProjectPackagesSameName_BothSkipped()
    {
        // Two project packages with the same name cannot be distinguished so
        // both are skipped. A non-colliding sibling continues to load.
        CreateProjectPackage("dup-a", "dup-tool", "Dup A", "custom", ".a");
        CreateProjectPackage("dup-b", "dup-tool", "Dup B", "custom", ".b");
        CreateProjectPackage("legit", "other-tool", "Legit", "custom", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllDocumentEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("other-tool");
    }

    [Test]
    public async Task RegisterPackages_LoadFailures_SendPackageLoadErrorMessage()
    {
        CreateProjectPackage("dup-a", "dup-tool", "Dup A", "custom", ".a");
        CreateProjectPackage("dup-b", "dup-tool", "Dup B", "custom", ".b");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _messengerService.Received(1).Send(Arg.Is<ConsoleErrorMessage>(m => m.ErrorType == ConsoleErrorType.PackageLoadError));
    }

    [Test]
    public async Task RegisterPackages_RecordsDiscoveryInProjectLoadReport()
    {
        // The error banner points users at the project load report, so the
        // discovery outcome must be recorded and flushed during registration.
        CreateProjectPackage("good", "good", "Good", "custom", ".good");
        var badDir = Path.Combine(_tempProjectFolder, "packages", "bad");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "package.toml"), "{ invalid toml }");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _loadReporter.Received(1).RecordPackageReport(Arg.Is<PackageDiscoveryReport>(r =>
            r.ProjectPackageCount == 1 &&
            r.Failures.Count == 1 &&
            r.Failures[0].Reason == PackageLoadFailureReason.InvalidManifest &&
            !string.IsNullOrEmpty(r.Failures[0].Detail)));
        await _loadReporter.Received(1).FlushAsync();
    }

    [Test]
    public async Task RegisterPackages_NoFailures_DoesNotSendPackageLoadErrorMessage()
    {
        CreateProjectPackage("legit", "legit", "Legit", "custom", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _messengerService.DidNotReceive().Send(Arg.Is<ConsoleErrorMessage>(m => m.ErrorType == ConsoleErrorType.PackageLoadError));
    }

    [Test]
    public async Task RegisterPackages_InvalidBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "bad-bundled");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllText(Path.Combine(bundledDir, "package.toml"), "{ invalid toml }");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_MissingBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "no-manifest-bundled");
        Directory.CreateDirectory(bundledDir);

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllDocumentEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ClearsPreviousContributions()
    {
        CreateProjectPackage("editor-a", "editor-a", "Editor A", "custom", ".a");

        await _service.RegisterPackagesAsync(_tempProjectFolder);
        _service.GetAllDocumentEditors().Should().HaveCount(1);

        // Create a second temp folder with different packages
        var secondFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageServiceTests) + "_2");
        try
        {
            Directory.CreateDirectory(secondFolder);

            // Repoint the workspace-bound gateway at the second folder so the
            // second discovery probes secondFolder/packages instead of the original.
            _resourceRegistry.ProjectFolderPath.Returns(secondFolder);
            _resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
            {
                var key = callInfo.Arg<ResourceKey>();
                return Result<string>.Ok(Path.Combine(secondFolder, key.Path.Replace('/', Path.DirectorySeparatorChar)));
            });

            // Discover from empty folder - should clear previous contributions
            await _service.RegisterPackagesAsync(secondFolder);
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

    [Test]
    public async Task GetContributingPackage_KnownEditorId_ReturnsThePackage()
    {
        var bundledDir = CreateBundledPackage("notes-pkg", "celbridge.notes", "Notes", "custom", ".note");
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        // CustomDocumentViewFactory builds editor IDs as "{packageName}.{contributionId}".
        // The contributionId comes from the [document] table key in package.toml,
        // which CreateBundledPackage sets to the docType argument.
        var editorId = new DocumentEditorId("celbridge.notes.custom");

        var package = _service.GetContributingPackage(editorId);

        package.Should().NotBeNull();
        package!.Info.Name.Should().Be("celbridge.notes");
    }

    [Test]
    public async Task GetContributingPackage_UnknownEditorId_ReturnsNull()
    {
        CreateProjectPackage("known", "known", "Known", "custom", ".known");
        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var package = _service.GetContributingPackage(new DocumentEditorId("thirdparty.binary-editor"));

        package.Should().BeNull();
    }

    [Test]
    public async Task GetContributingPackage_DistinguishesPackagesWithDottedNamePrefixes()
    {
        // A naive split-on-first-dot would mismatch "celbridge.notes.custom" against
        // a package whose name is just "celbridge". The lookup must match the longest
        // package-name prefix, not split heuristically.
        var notesDir = CreateBundledPackage("notes-pkg", "celbridge.notes", "Notes", "custom", ".note");
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = notesDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var package = _service.GetContributingPackage(new DocumentEditorId("celbridge.notes.custom"));

        package.Should().NotBeNull();
        package!.Info.Name.Should().Be("celbridge.notes");
    }

    /// <summary>
    /// Creates a package in the project's packages/ folder with a standard structure.
    /// </summary>
    private string CreateProjectPackage(string dirName, string packageName, string packageTitle, string docType, string fileExt)
    {
        var packageDir = Path.Combine(_tempProjectFolder, "packages", dirName);
        Directory.CreateDirectory(packageDir);
        WritePackageFiles(packageDir, packageName, packageTitle, docType, fileExt);
        return packageDir;
    }

    /// <summary>
    /// Creates a bundled package in a standalone folder.
    /// </summary>
    private string CreateBundledPackage(string dirName, string packageName, string packageTitle, string docType, string fileExt)
    {
        var packageDir = Path.Combine(_tempProjectFolder, dirName);
        Directory.CreateDirectory(packageDir);
        WritePackageFiles(packageDir, packageName, packageTitle, docType, fileExt);
        return packageDir;
    }

    private static void WritePackageFiles(string packageDir, string packageName, string packageTitle, string docType, string fileExt)
    {
        File.WriteAllText(Path.Combine(packageDir, "package.toml"), $"""
            [package]
            name = "{packageName}"
            title = "{packageTitle}"

            [contributes]
            document_editors = ["editor.document.toml"]
            """);

        File.WriteAllText(Path.Combine(packageDir, "editor.document.toml"), $"""
            [document]
            id = "{packageName}-doc"
            type = "{docType}"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = "{fileExt}"
            display_name = "TestFileType"
            """);
    }
}
