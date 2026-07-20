using Celbridge.Console;
using Celbridge.FileSystem.Services;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.Projects.Services;
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
    private IProjectService _projectService = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageServiceTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var logger = Substitute.For<ILogger<PackageRegistry>>();
        _messengerService = Substitute.For<IMessengerService>();
        _moduleService = Substitute.For<IModuleService>();
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor>());

        // Discovery is independent of the project config. Tests that exercise declared
        // instances call SetProjectConfig to supply an activation list and declarations.
        _projectService = Substitute.For<IProjectService>();
        _projectService.CurrentProject.Returns((IProject?)null);

        // The registry reconciles and persists through IProjectService; mirror the real delegation to
        // the static reconciler so the mocked service produces the same active set.
        _projectService.ReconcileConfigAsync(Arg.Any<IReadOnlyList<EditorContribution>>(), Arg.Any<bool>())
            .Returns(callInfo =>
            {
                var config = _projectService.CurrentProject?.Config;
                if (config is null)
                {
                    return Task.FromResult<ProjectConfigReconcileResult?>(null);
                }
                return Task.FromResult<ProjectConfigReconcileResult?>(
                    ProjectConfigReconciler.Reconcile(config, callInfo.Arg<IReadOnlyList<EditorContribution>>()));
            });

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

        var registry = new PackageRegistry(logger, _moduleService, localizationService, workspaceWrapper, _projectService, fileSystem);
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
    public async Task GetEditorInstances_MultipleDiscoveredPackages_ResolveInDiscoveryOrder()
    {
        // Activation is discovery-driven, so both default-active packages materialize instances
        // whose order follows discovery order (ordinal folder enumeration).
        CreateProjectPackage("editor-a", "editor-a", "Editor A", ".a");
        CreateProjectPackage("editor-b", "editor-b", "Editor B", ".b");
        SetProjectConfig();

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var instances = _service.GetEditorInstances();
        instances.Should().HaveCount(2);
        instances[0].InstanceId.Should().Be(EditorInstanceId.Create("editor-a", "editor"));
        instances[1].InstanceId.Should().Be(EditorInstanceId.Create("editor-b", "editor"));
    }

    [Test]
    public async Task GetEditorInstances_DiscoveredDefaultActivePackage_MaterializesDefaultInstance()
    {
        // A discovered default-active contribution materializes an instance with no project-file entry.
        CreateProjectPackage("my-editor", "my-editor", "My Editor", ".myext");
        SetProjectConfig();

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllEditors().Should().HaveCount(1);
        _service.GetEditorInstances().Should().ContainSingle()
            .Which.InstanceId.Should().Be(EditorInstanceId.Create("my-editor", "editor"));
    }

    [Test]
    public async Task GetEditorInstances_OrphanedOverride_IsDroppedWithWarning()
    {
        // The override references a package that was never discovered, so reconcile drops it.
        SetProjectConfig(CreateOverride("ghost-editor"));

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetEditorInstances().Should().BeEmpty();
        _loadReporter.Received(1).RecordPackageReport(Arg.Is<PackageDiscoveryReport>(report =>
            report.EditorInstanceWarnings.Count == 1
            && report.EditorInstanceWarnings[0].Detail.Contains("ghost-editor")));
    }

    [Test]
    public async Task GetEditorInstances_UnknownContributionOverride_IsDroppedAndDefaultStillMaterializes()
    {
        CreateProjectPackage("my-editor", "my-editor", "My Editor", ".myext");
        SetProjectConfig(CreateOverride("my-editor", contributionId: "nonexistent"));

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        // The bogus override is dropped with a warning, but the package's real editor still
        // materializes its default instance.
        _service.GetEditorInstances().Should().ContainSingle()
            .Which.InstanceId.Should().Be(EditorInstanceId.Create("my-editor", "editor"));
        _loadReporter.Received(1).RecordPackageReport(Arg.Is<PackageDiscoveryReport>(report =>
            report.EditorInstanceWarnings.Count == 1
            && report.EditorInstanceWarnings[0].Detail.Contains("nonexistent")));
    }

    [Test]
    public async Task GetEditorInstances_OverrideConfig_MergesOverDescriptorDefaults()
    {
        CreateConfigurablePackage("console", "console");
        var contributionOverride = new ContributionOverride
        {
            PackageName = "console",
            ContributionId = "editor",
            Config = new Dictionary<string, object?>
            {
                ["shell"] = "pwsh",
                ["dependencies"] = new List<string> { "numpy" }
            }
        };
        SetProjectConfig(contributionOverride);

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var instance = _service.GetEditorInstances().Should().ContainSingle().Subject;

        // The override's own values take precedence over the descriptor defaults, and a string list is
        // delivered on the Options channel as a JSON array.
        instance.Config["shell"].Should().Be("pwsh");
        instance.Config["dependencies"].Should().Be("[\"numpy\"]");

        // A descriptor default the override did not set still reaches the editor.
        instance.Config["scrollback"].Should().Be("500");
    }

    [Test]
    public async Task GetEditorInstances_InvalidConfigKeys_AreDroppedAndReportedAsWarnings()
    {
        CreateConfigurablePackage("console", "console");
        var contributionOverride = new ContributionOverride
        {
            PackageName = "console",
            ContributionId = "editor",
            Config = new Dictionary<string, object?>
            {
                // Not one of the declared enum values.
                ["shell"] = "fish",
                // Not a declared descriptor key at all.
                ["nonsense"] = "value"
            }
        };
        SetProjectConfig(contributionOverride);

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        // The instance still loads: a typo degrades one setting, not the instance.
        var instance = _service.GetEditorInstances().Should().ContainSingle().Subject;
        instance.Config["shell"].Should().Be("python");
        instance.Config.Should().NotContainKey("nonsense");

        _loadReporter.Received(1).RecordPackageReport(Arg.Is<PackageDiscoveryReport>(report =>
            report.EditorInstanceWarnings.Any(warning => warning.Detail.Contains("shell"))
            && report.EditorInstanceWarnings.Any(warning => warning.Detail.Contains("nonsense"))));
    }

    [Test]
    public async Task GetBuiltInEditors_AlwaysActivePackage_IsPresentWithoutActivationEntry()
    {
        var bundledDir = CreateBuiltInCodeEditorPackage();
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });
        SetProjectConfig();

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var builtIns = _service.GetBuiltInEditors();
        builtIns.Should().Contain(builtIn => builtIn.InstanceId == BuiltInEditors.CodeEditorId);
    }

    [Test]
    public async Task GetBuiltInEditors_OptionalBuiltInPackage_IsPresentWithoutActivationEntry()
    {
        // The spreadsheet package ships in the installer, so its editor is a built-in that the
        // project never activates or declares.
        var bundledDir = CreateBuiltInSpreadsheetPackage();
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });
        SetProjectConfig();

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetBuiltInEditors().Should().Contain(builtIn => builtIn.InstanceId == BuiltInEditors.SpreadsheetEditorId);
    }

    [Test]
    public async Task GetBuiltInEditors_OptionalBuiltInPackageAbsent_IsSkippedAndOthersStillResolve()
    {
        // A source build without the SpreadJS library never discovers the spreadsheet package.
        // Its editor degrades to being unavailable, and the required built-ins are unaffected.
        var bundledDir = CreateBuiltInCodeEditorPackage();
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });
        SetProjectConfig();

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var builtIns = _service.GetBuiltInEditors();
        builtIns.Should().NotContain(builtIn => builtIn.InstanceId == BuiltInEditors.SpreadsheetEditorId);
        builtIns.Should().Contain(builtIn => builtIn.InstanceId == BuiltInEditors.CodeEditorId);
    }

    [Test]
    public async Task RegisterPackages_NoPackagesFolder_ReturnsEmpty()
    {
        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_EmptyPackagesFolder_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempProjectFolder, "packages"));

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ValidManifest_ReturnsManifest()
    {
        CreateProjectPackage("my-editor", "my-editor", "My Editor", ".myext");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("My Editor");
        contributions[0].Should().BeOfType<EditorContribution>();
    }

    [Test]
    public async Task RegisterPackages_MultiplePackages_ReturnsAll()
    {
        CreateProjectPackage("editor-a", "editor-a", "Editor A", ".a");
        CreateProjectPackage("editor-b", "editor-b", "Editor B", ".b");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Package.Title).ToList();
        names.Should().Contain("Editor A");
        names.Should().Contain("Editor B");
    }

    [Test]
    public async Task RegisterPackages_InvalidManifest_SkipsAndContinues()
    {
        CreateProjectPackage("good", "good", "Good", ".good");

        var badDir = Path.Combine(_tempProjectFolder, "packages", "bad");
        Directory.CreateDirectory(badDir);
        File.WriteAllText(Path.Combine(badDir, "package.toml"), "{ invalid toml }");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Good");
    }

    [Test]
    public async Task RegisterPackages_FolderWithoutManifest_IsSkipped()
    {
        CreateProjectPackage("with-manifest", "with-manifest", "Found", ".found");

        var folderWithoutManifest = Path.Combine(_tempProjectFolder, "packages", "no-manifest");
        Directory.CreateDirectory(folderWithoutManifest);

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Found");
    }

    [Test]
    public async Task RegisterPackages_IncludesModulePackages()
    {
        var bundledDir = CreateBundledPackage("bundled-editor", "celbridge.bundled", "Bundled", ".bnd");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Bundled");
    }

    [Test]
    public async Task RegisterPackages_CombinesProjectAndBundled()
    {
        CreateProjectPackage("proj-editor", "proj", "Project", ".proj");
        var bundledDir = CreateBundledPackage("bundled-editor", "celbridge.bundled", "Bundled", ".bnd");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(2);
        var names = contributions.Select(m => m.Package.Title).ToList();
        names.Should().Contain("Project");
        names.Should().Contain("Bundled");
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageWithReservedNamePrefix_Skipped()
    {
        // Project packages may not claim a name under the reserved "celbridge." namespace.
        CreateProjectPackage("impostor", "celbridge.notes", "Impostor Notes", ".imp");
        CreateProjectPackage("legit", "legit", "Legit", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("legit");
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageWithMixedCaseName_RejectedByFormatValidation()
    {
        // Package names are lowercase-only. A mixed-case name fails manifest validation
        // before the reserved-prefix check runs, so "Celbridge.Something" cannot be
        // used as a workaround for the prefix block.
        CreateProjectPackage("mixed-case", "Celbridge.Something", "Mixed Case", ".mc");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageWithDottedName_Skipped()
    {
        // Project packages cannot claim a dotted name. Only flat global-namespace
        // names are permitted for project packages.
        CreateProjectPackage("dotted", "acme.tool", "Dotted", ".dot");
        CreateProjectPackage("flat", "legit-tool", "Flat", ".flat");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("legit-tool");
    }

    [TestCase(".cel")]
    [TestCase(".foo.cel")]
    public async Task RegisterPackages_ProjectPackageWithCelExtension_Skipped(string reservedExtension)
    {
        // Document types cannot register inside the reserved .cel namespace.
        // The check rejects both the bare suffix and multi-part forms that end
        // in .cel.
        CreateProjectPackage("reserved", "reserved-ext", "Reserved", reservedExtension);
        CreateProjectPackage("legit", "legit", "Legit", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("legit");
    }

    [Test]
    public async Task RegisterPackages_PackageWithCelInMiddleSegment_Accepted()
    {
        // Only the trailing extension is reserved. .cel.bar ends in .bar and
        // does not conflict with sidecar pairing — the editor binds normally
        // and the sidecar lives next to the parent at <name>.cel.bar.cel.
        CreateProjectPackage("midcel", "mid-cel", "MidCel", ".cel.bar");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("mid-cel");
    }

    [Test]
    public async Task RegisterPackages_BundledPackageWithCelExtension_Skipped()
    {
        // The reservation applies to bundled and project packages alike.
        var reservedDir = CreateBundledPackage("bundled-reserved", "celbridge.reserved", "Bundled Reserved", ".cel");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = reservedDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_BundledPackageWithReservedNamePrefix_Allowed()
    {
        // Bundled packages are the intended owners of the "celbridge." namespace.
        var bundledDir = CreateBundledPackage("bundled-official", "celbridge.notes", "Official Notes", ".note");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("celbridge.notes");
    }

    [Test]
    public async Task RegisterPackages_TwoBundledPackagesSameName_BothSkipped()
    {
        // Two bundled packages with the same name is a first-party build bug.
        // Both are skipped rather than silently picking a winner.
        var dirA = CreateBundledPackage("bundled-a", "celbridge.conflict", "Conflict A", ".a");
        var dirB = CreateBundledPackage("bundled-b", "celbridge.conflict", "Conflict B", ".b");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor>
        {
            new() { Folder = dirA },
            new() { Folder = dirB }
        });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ProjectPackageConflictsWithBundled_ProjectSkipped()
    {
        // Bundled wins over project when names collide. Both use flat names here
        // so the collision check is what rejects the project package, not the
        // reserved-prefix or unregistered-namespace rules.
        var bundledDir = CreateBundledPackage("bundled", "shared-id", "Bundled", ".bnd");
        CreateProjectPackage("project", "shared-id", "Project", ".prj");

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Title.Should().Be("Bundled");
    }

    [Test]
    public async Task RegisterPackages_TwoProjectPackagesSameName_BothSkipped()
    {
        // Two project packages with the same name cannot be distinguished so
        // both are skipped. A non-colliding sibling continues to load.
        CreateProjectPackage("dup-a", "dup-tool", "Dup A", ".a");
        CreateProjectPackage("dup-b", "dup-tool", "Dup B", ".b");
        CreateProjectPackage("legit", "other-tool", "Legit", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("other-tool");
    }

    [Test]
    public async Task GetLoadFailures_AfterDuplicateName_ReportsCollidingPackages()
    {
        CreateProjectPackage("dup-a", "dup-tool", "Dup A", ".a");
        CreateProjectPackage("dup-b", "dup-tool", "Dup B", ".b");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var duplicateFailures = _service.GetLoadFailures()
            .Where(failure => failure.Reason == PackageLoadFailureReason.DuplicateName)
            .ToList();
        duplicateFailures.Should().HaveCount(2);
        duplicateFailures.Should().OnlyContain(failure => failure.PackageName == "dup-tool");
    }

    [Test]
    public async Task GetLoadFailures_AllValid_IsEmpty()
    {
        CreateProjectPackage("legit", "legit", "Legit", ".legit");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetLoadFailures().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_NestedManifest_DiscoveredOutsidePackagesFolder()
    {
        // Discovery is not tied to packages/: a manifest anywhere under the project
        // root is found, honouring the gateway's visibility rules.
        var toolsDir = Path.Combine(_tempProjectFolder, "tools", "my-editor");
        Directory.CreateDirectory(toolsDir);
        WritePackageFiles(toolsDir, "nested-tool", "Nested Tool", ".nst");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var contributions = _service.GetAllEditors();
        contributions.Should().HaveCount(1);
        contributions[0].Package.Name.Should().Be("nested-tool");
    }

    [Test]
    public async Task RegisterPackages_LoadFailures_SendPackageLoadErrorMessage()
    {
        CreateProjectPackage("dup-a", "dup-tool", "Dup A", ".a");
        CreateProjectPackage("dup-b", "dup-tool", "Dup B", ".b");

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _messengerService.Received(1).Send(Arg.Is<ConsoleErrorMessage>(m => m.ErrorType == ConsoleErrorType.PackageLoadError));
    }

    [Test]
    public async Task RegisterPackages_RecordsDiscoveryInProjectLoadReport()
    {
        CreateProjectPackage("good", "good", "Good", ".good");
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
        CreateProjectPackage("legit", "legit", "Legit", ".legit");

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

        _service.GetAllEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_MissingBundledManifestSkipped()
    {
        var bundledDir = Path.Combine(_tempProjectFolder, "no-manifest-bundled");
        Directory.CreateDirectory(bundledDir);

        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        _service.GetAllEditors().Should().BeEmpty();
    }

    [Test]
    public async Task RegisterPackages_ClearsPreviousContributions()
    {
        CreateProjectPackage("editor-a", "editor-a", "Editor A", ".a");

        await _service.RegisterPackagesAsync(_tempProjectFolder);
        _service.GetAllEditors().Should().HaveCount(1);

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

            await _service.RegisterPackagesAsync(secondFolder);
            _service.GetAllEditors().Should().BeEmpty();
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
    public async Task GetContributingPackage_ActiveContributionId_ReturnsThePackage()
    {
        var bundledDir = CreateBundledPackage("notes-pkg", "celbridge.notes", "Notes", ".note");
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });
        SetProjectConfig();

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var package = _service.GetContributingPackage(EditorInstanceId.Create("celbridge.notes", "editor"));

        package.Should().NotBeNull();
        package!.Info.Name.Should().Be("celbridge.notes");
    }

    [Test]
    public async Task GetContributingPackage_UnknownEditorId_ReturnsNull()
    {
        CreateProjectPackage("known", "known", "Known", ".known");
        SetProjectConfig();
        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var package = _service.GetContributingPackage(new EditorInstanceId("binary-editor"));

        package.Should().BeNull();
    }

    [Test]
    public async Task GetContributingPackage_PackageNameWithoutContribution_ReturnsNull()
    {
        // Editors are addressed by the "{package}.{contribution}" reference, not the bare package
        // name, so a lookup by package name alone resolves to nothing.
        var bundledDir = CreateBundledPackage("notes-pkg", "celbridge.notes", "Notes", ".note");
        _moduleService.GetBundledPackages().Returns(new List<BundledPackageDescriptor> { new() { Folder = bundledDir } });
        SetProjectConfig();

        await _service.RegisterPackagesAsync(_tempProjectFolder);

        var package = _service.GetContributingPackage(new EditorInstanceId("celbridge.notes"));

        package.Should().BeNull();
    }

    /// <summary>
    /// Creates a package in the project's packages/ folder with a standard structure.
    /// </summary>
    private string CreateProjectPackage(string dirName, string packageName, string packageTitle, string fileExt)
    {
        var packageDir = Path.Combine(_tempProjectFolder, "packages", dirName);
        Directory.CreateDirectory(packageDir);
        WritePackageFiles(packageDir, packageName, packageTitle, fileExt);
        return packageDir;
    }

    /// <summary>
    /// Creates a bundled package in a standalone folder.
    /// </summary>
    private string CreateBundledPackage(string dirName, string packageName, string packageTitle, string fileExt)
    {
        var packageDir = Path.Combine(_tempProjectFolder, dirName);
        Directory.CreateDirectory(packageDir);
        WritePackageFiles(packageDir, packageName, packageTitle, fileExt);
        return packageDir;
    }

    /// <summary>
    /// Writes a package manifest and a single document-editor manifest whose contribution id is
    /// always "editor".
    /// </summary>
    private static void WritePackageFiles(string packageDir, string packageName, string packageTitle, string fileExt)
    {
        File.WriteAllText(Path.Combine(packageDir, "package.toml"), $"""
            [package]
            name = "{packageName}"
            title = "{packageTitle}"

            [contributes]
            editors = ["editor.editor.toml"]
            """);

        File.WriteAllText(Path.Combine(packageDir, "editor.editor.toml"), $"""
            [editor]
            id = "editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = "{fileExt}"
            display-name = "TestFileType"
            """);
    }

    /// <summary>
    /// Creates a project package whose editor declares config descriptors of several types.
    /// </summary>
    private void CreateConfigurablePackage(string dirName, string packageName)
    {
        var packageDir = Path.Combine(_tempProjectFolder, "packages", dirName);
        Directory.CreateDirectory(packageDir);

        File.WriteAllText(Path.Combine(packageDir, "package.toml"), $"""
            [package]
            name = "{packageName}"
            title = "Console"

            [contributes]
            editors = ["editor.editor.toml"]
            """);

        File.WriteAllText(Path.Combine(packageDir, "editor.editor.toml"), """
            [editor]
            id = "editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".console"
            display-name = "TestFileType"

            [[config]]
            key = "shell"
            type = "enum"
            values = ["python", "pwsh"]
            default = "python"
            display-name = "Console_Config_Shell"

            [[config]]
            key = "dependencies"
            type = "string-list"
            default = []
            display-name = "Console_Config_Dependencies"

            [[config]]
            key = "scrollback"
            type = "number"
            default = 500
            display-name = "Console_Config_Scrollback"
            """);
    }

    /// <summary>
    /// Creates a bundled package standing in for the real code-editor package, whose "code"
    /// contribution backs the built-in code editor.
    /// </summary>
    private string CreateBuiltInCodeEditorPackage()
    {
        var packageDir = Path.Combine(_tempProjectFolder, "code-editor-pkg");
        Directory.CreateDirectory(packageDir);

        File.WriteAllText(Path.Combine(packageDir, "package.toml"), """
            [package]
            name = "celbridge.code-editor"
            title = "Code Editor"

            [contributes]
            editors = ["code.editor.toml"]
            """);

        File.WriteAllText(Path.Combine(packageDir, "code.editor.toml"), """
            [editor]
            id = "code"
            type = "document"
            display-name = "CodeEditor_Editor_Code"

            [[file-types]]
            extension = ".txt"
            display-name = "CodeEditor_FileType_Code"
            """);

        return packageDir;
    }

    /// <summary>
    /// Creates a bundled package standing in for the real spreadsheet package, whose
    /// "spreadsheet" contribution backs the optional built-in spreadsheet editor.
    /// </summary>
    private string CreateBuiltInSpreadsheetPackage()
    {
        var packageDir = Path.Combine(_tempProjectFolder, "spreadsheet-pkg");
        Directory.CreateDirectory(packageDir);

        File.WriteAllText(Path.Combine(packageDir, "package.toml"), """
            [package]
            name = "celbridge.spreadsheet"
            title = "Spreadsheet"

            [contributes]
            editors = ["spreadsheet.editor.toml"]
            """);

        File.WriteAllText(Path.Combine(packageDir, "spreadsheet.editor.toml"), """
            [editor]
            id = "spreadsheet"
            type = "document"
            binary = true
            display-name = "Spreadsheet_Editor_Name"

            [[file-types]]
            extension = ".xlsx"
            display-name = "Spreadsheet_FileType_Xlsx"
            """);

        return packageDir;
    }

    /// <summary>
    /// Points the project service at a config with the given per-contribution overrides. Activation is
    /// discovery-driven, so the config carries only the project's overrides of the discovered
    /// defaults.
    /// </summary>
    private void SetProjectConfig(params ContributionOverride[] overrides)
    {
        var config = new ProjectConfig
        {
            ContributionOverrides = overrides
        };

        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ConfigIsHealthy.Returns(true);
        _projectService.CurrentProject.Returns(project);
    }

    private static ContributionOverride CreateOverride(
        string packageName,
        string contributionId = "editor")
    {
        return new ContributionOverride
        {
            PackageName = packageName,
            ContributionId = contributionId
        };
    }
}
