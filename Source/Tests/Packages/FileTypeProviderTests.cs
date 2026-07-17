using Celbridge.FileSystem.Services;
using Celbridge.Packages;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Settings;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class PackageServiceDocumentTypeTests
{
    private string _tempProjectFolder = null!;
    private PackageService _service = null!;
    private IModuleService _moduleService = null!;
    private IProjectService _projectService = null!;
    private List<string> _bundledPackagePaths = null!;
    private List<string> _activatedPackages = null!;
    private List<EditorInstanceDeclaration> _instanceDeclarations = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(PackageServiceDocumentTypeTests));
        Directory.CreateDirectory(_tempProjectFolder);

        _bundledPackagePaths = [];
        _moduleService = Substitute.For<IModuleService>();
        _moduleService.GetBundledPackages().Returns(_ => _bundledPackagePaths
            .Select(folder => new BundledPackageDescriptor { Folder = folder })
            .ToList());

        // Document types come from the available editors, so each created package is activated
        // and instantiated unless a test opts out.
        _activatedPackages = [];
        _instanceDeclarations = [];
        _projectService = Substitute.For<IProjectService>();
        var project = Substitute.For<IProject>();
        project.Config.Returns(_ => new ProjectConfig
        {
            Celbridge = new CelbridgeSection { Packages = _activatedPackages },
            Instances = _instanceDeclarations
        });
        _projectService.CurrentProject.Returns(project);

        var logger = Substitute.For<ILogger<PackageRegistry>>();
        var messengerService = Substitute.For<IMessengerService>();

        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ProjectFolderPath.Returns(_tempProjectFolder);
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var key = callInfo.Arg<ResourceKey>();
            return Result<string>.Ok(Path.Combine(_tempProjectFolder, key.Path.Replace('/', Path.DirectorySeparatorChar)));
        });
        // The package walk enumerates the project tree through the gateway, which resolves
        // with validateCase:false. Stub the two-argument overload too.
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(callInfo =>
        {
            var key = callInfo.Arg<ResourceKey>();
            return Result<string>.Ok(Path.Combine(_tempProjectFolder, key.Path.Replace('/', Path.DirectorySeparatorChar)));
        });
        resourceRegistry.GetResourceKey(Arg.Any<string>()).Returns(callInfo =>
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
        resourceService.Registry.Returns(resourceRegistry);

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
        var loadReporter = Substitute.For<IProjectLoadReporter>();
        _service = new PackageService(messengerService, loadReporter, registry);
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
    public void GetDocumentTypes_NoPackages_ReturnsEmpty()
    {
        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().BeEmpty();
    }

    [Test]
    public async Task GetDocumentTypes_PackageWithTemplates_ReturnsDocumentType()
    {
        await CreateBundledPackage(
            "test-editor",
            "TestEditor",
            [(".test", "TestEditor")],
            templates:
            [
                ("empty", "Empty", "templates/empty.test", true)
            ]);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().HaveCount(1);
        documentTypes[0].FileExtensions.Should().Contain(".test");
        documentTypes[0].DisplayName.Should().Be("TestEditor");
    }

    [Test]
    public async Task GetDocumentTypes_PackageWithoutTemplates_Excluded()
    {
        await CreateBundledPackage("no-templates", "NoTemplates", [(".notemplate", "NoTemplates")], templates: null);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().BeEmpty();
    }

    [Test]
    public async Task GetDocumentTypes_WithLocalization_ResolvesDisplayName()
    {
        await CreateBundledPackage(
            "note",
            "Note",
            [(".note", "Note_FileType_Note")],
            templates:
            [
                ("empty", "Note_Template_Empty", "templates/empty.note", true)
            ],
            localizationStrings: new Dictionary<string, string>
            {
                ["Note_FileType_Note"] = "My Localized Note"
            });

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().HaveCount(1);
        documentTypes[0].DisplayName.Should().Be("My Localized Note");
    }

    [Test]
    public async Task GetDocumentTypes_UninstantiatedContribution_Excluded()
    {
        // The package is discovered and activated, but the project declares no instance of its
        // editor, so it offers no creatable document type.
        await CreateBundledPackage(
            "undeclared-editor",
            "UndeclaredEditor",
            [(".undeclared", "UndeclaredEditor")],
            templates:
            [
                ("empty", "Empty", "templates/empty.undeclared", true)
            ],
            declareInstance: false);

        _service.GetAllEditors().Should().HaveCount(1);
        _service.GetDocumentTypes().Should().BeEmpty();
    }

    [Test]
    public async Task GetDocumentTypes_MultipleFileExtensions_AllIncluded()
    {
        await CreateBundledPackage(
            "multi-ext",
            "MultiExt",
            [(".md", "MultiExt"), (".markdown", "MultiExt")],
            templates:
            [
                ("empty", "Empty", "templates/empty.md", true)
            ]);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().HaveCount(1);
        documentTypes[0].FileExtensions.Should().HaveCount(2);
        documentTypes[0].FileExtensions.Should().Contain(".md");
        documentTypes[0].FileExtensions.Should().Contain(".markdown");
    }

    [Test]
    public async Task GetDocumentTypes_UtilityContribution_Excluded()
    {
        var packageDir = Path.Combine(_tempProjectFolder, "bundled", "emoji");
        Directory.CreateDirectory(packageDir);

        File.WriteAllText(Path.Combine(packageDir, "package.toml"), """
            [package]
            name = "test.emoji"
            title = "Emoji"

            [contributes]
            editors = ["emoji.editor.toml"]
            """);

        File.WriteAllText(Path.Combine(packageDir, "emoji.editor.toml"), """
            [editor]
            id = "emoji-renderer"
            type = "utility"
            entry-point = "index.html"

            [utility]
            resource-extension = "._emoji"
            template = "templates/default._emoji"
            icon = "emoji-smile"
            tooltip = "Emoji_Utility_Tooltip"
            """);

        _bundledPackagePaths.Add(packageDir);
        _activatedPackages.Add("test.emoji");
        _instanceDeclarations.Add(new EditorInstanceDeclaration
        {
            InstanceId = "emoji",
            PackageName = "test.emoji",
            ContributionId = "emoji-renderer"
        });
        await _service.RegisterPackagesAsync(_tempProjectFolder);

        // The utility is instantiated, but must not appear as a creatable New File type.
        var editors = _service.GetAllEditors();
        editors.Should().ContainSingle();
        editors[0].IsUtility.Should().BeTrue();
        _service.GetEditorInstances().Should().ContainSingle();

        _service.GetDocumentTypes().Should().BeEmpty();
    }

    [Test]
    public async Task GetDefaultTemplateContent_PackageWithTemplate_ReturnsContent()
    {
        var templateContent = "{\"type\":\"doc\"}";
        await CreateBundledPackage(
            "note",
            "Note",
            [(".note", "Note")],
            templates:
            [
                ("empty", "Empty", "templates/empty.note", true)
            ],
            templateFiles: new Dictionary<string, string>
            {
                ["templates/empty.note"] = templateContent
            });

        var content = _service.GetDefaultTemplateContent(".note");

        content.Should().NotBeNull();
        var text = System.Text.Encoding.UTF8.GetString(content!);
        text.Should().Be(templateContent);
    }

    [Test]
    public void GetDefaultTemplateContent_NoMatchingExtension_ReturnsNull()
    {
        var content = _service.GetDefaultTemplateContent(".unknown");

        content.Should().BeNull();
    }

    [Test]
    public async Task GetDefaultTemplateContent_PackageWithoutDefaultTemplate_ReturnsNull()
    {
        await CreateBundledPackage(
            "non-default",
            "NonDefault",
            [(".nd", "NonDefault")],
            templates:
            [
                ("example", "Example", "templates/example.nd", false)
            ]);

        var content = _service.GetDefaultTemplateContent(".nd");

        content.Should().BeNull();
    }

    [Test]
    public async Task GetDefaultTemplateContent_CaseInsensitiveExtension()
    {
        var templateContent = "template content";
        await CreateBundledPackage(
            "case-test",
            "CaseTest",
            [(".TEST", "CaseTest")],
            templates:
            [
                ("empty", "Empty", "templates/empty.test", true)
            ],
            templateFiles: new Dictionary<string, string>
            {
                ["templates/empty.test"] = templateContent
            });

        var content = _service.GetDefaultTemplateContent(".test");

        content.Should().NotBeNull();
    }

    [Test]
    public async Task GetDefaultTemplateContent_NoProject_StillFindsBundled()
    {
        await CreateBundledPackage(
            "orphan",
            "Orphan",
            [(".orphan", "Orphan")],
            templates:
            [
                ("empty", "Empty", "templates/empty.orphan", true)
            ],
            templateFiles: new Dictionary<string, string>
            {
                ["templates/empty.orphan"] = "content"
            });

        var content = _service.GetDefaultTemplateContent(".orphan");
        content.Should().NotBeNull();
    }

    /// <summary>
    /// Helper to create a bundled package folder with TOML manifests and optional files.
    /// Registers the path with the module service mock, activates the package, declares an
    /// instance of its editor unless declareInstance is false, and re-discovers packages.
    /// </summary>
    private async Task CreateBundledPackage(
        string dirName,
        string packageName,
        (string Extension, string DisplayName)[] fileTypes,
        (string Id, string DisplayName, string File, bool Default)[]? templates = null,
        Dictionary<string, string>? localizationStrings = null,
        Dictionary<string, string>? templateFiles = null,
        bool declareInstance = true)
    {
        var packageDir = Path.Combine(_tempProjectFolder, "bundled", dirName);
        Directory.CreateDirectory(packageDir);

        var bundledName = $"test.{dirName}";

        File.WriteAllText(Path.Combine(packageDir, "package.toml"), $"""
            [package]
            name = "{bundledName}"
            title = "{packageName}"

            [contributes]
            editors = ["editor.editor.toml"]
            """);

        var fileTypesToml = string.Join("\n", fileTypes.Select(ft => $"""
            [[file-types]]
            extension = "{ft.Extension}"
            display-name = "{ft.DisplayName}"
            """));

        var templatesToml = "";
        if (templates is not null)
        {
            templatesToml = string.Join("\n", templates.Select(t => $"""
                [[templates]]
                id = "{t.Id}"
                display-name = "{t.DisplayName}"
                template-file = "{t.File}"
                default = {t.Default.ToString().ToLower()}
                """));
        }

        File.WriteAllText(Path.Combine(packageDir, "editor.editor.toml"), $"""
            [editor]
            id = "editor"
            type = "document"
            entry-point = "index.html"
            display-name = "{packageName}"

            {fileTypesToml}

            {templatesToml}
            """);

        _activatedPackages.Add(bundledName);
        if (declareInstance)
        {
            _instanceDeclarations.Add(new EditorInstanceDeclaration
            {
                InstanceId = dirName,
                PackageName = bundledName,
                ContributionId = "editor"
            });
        }

        if (localizationStrings is not null)
        {
            var localizationFolder = Path.Combine(packageDir, PackageLocalizationService.LocalizationFolder);
            Directory.CreateDirectory(localizationFolder);

            var entries = localizationStrings.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\"");
            var localizationJson = $"{{ {string.Join(", ", entries)} }}";
            File.WriteAllText(Path.Combine(localizationFolder, "en.json"), localizationJson);
        }

        if (templateFiles is not null)
        {
            foreach (var kvp in templateFiles)
            {
                var filePath = Path.Combine(packageDir, kvp.Key);
                var folder = Path.GetDirectoryName(filePath)!;
                Directory.CreateDirectory(folder);
                File.WriteAllText(filePath, kvp.Value);
            }
        }

        _bundledPackagePaths.Add(packageDir);
        await _service.RegisterPackagesAsync(_tempProjectFolder);
    }
}
