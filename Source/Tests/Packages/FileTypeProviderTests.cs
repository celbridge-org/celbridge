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
    private IFeatureFlags _featureFlags = null!;
    private List<string> _bundledPackagePaths = null!;

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

        _featureFlags = Substitute.For<IFeatureFlags>();
        _featureFlags.IsEnabled(Arg.Any<string>()).Returns(true);

        var logger = Substitute.For<ILogger<PackageRegistry>>();
        var messengerService = Substitute.For<IMessengerService>();

        var resourceRegistry = Substitute.For<IResourceRegistry>();
        resourceRegistry.ProjectFolderPath.Returns(_tempProjectFolder);
        resourceRegistry.ResolveResourcePath(Arg.Any<ResourceKey>()).Returns(callInfo =>
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

        var registry = new PackageRegistry(logger, _moduleService, _featureFlags, localizationService, workspaceWrapper, fileSystem);
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
    public async Task GetDocumentTypes_DisabledFeatureFlag_Excluded()
    {
        await CreateBundledPackage(
            "flagged-editor",
            "FlaggedEditor",
            [(".flagged", "FlaggedEditor")],
            featureFlag: "my-flag",
            templates:
            [
                ("empty", "Empty", "templates/empty.flagged", true)
            ]);

        _featureFlags.IsEnabled("my-flag").Returns(false);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().BeEmpty();
    }

    [Test]
    public async Task GetDocumentTypes_EnabledFeatureFlag_Included()
    {
        await CreateBundledPackage(
            "flagged-editor",
            "FlaggedEditor",
            [(".flagged", "FlaggedEditor")],
            featureFlag: "my-flag",
            templates:
            [
                ("empty", "Empty", "templates/empty.flagged", true)
            ]);

        _featureFlags.IsEnabled("my-flag").Returns(true);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().HaveCount(1);
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

        // Should still find bundled packages even without a project
        var content = _service.GetDefaultTemplateContent(".orphan");
        content.Should().NotBeNull();
    }

    /// <summary>
    /// Helper to create a bundled package folder with TOML manifests and optional files.
    /// Registers the path with the module service mock and re-discovers packages.
    /// </summary>
    private async Task CreateBundledPackage(
        string dirName,
        string packageName,
        (string Extension, string DisplayName)[] fileTypes,
        (string Id, string DisplayName, string File, bool Default)[]? templates = null,
        string? featureFlag = null,
        Dictionary<string, string>? localizationStrings = null,
        Dictionary<string, string>? templateFiles = null)
    {
        var packageDir = Path.Combine(_tempProjectFolder, "bundled", dirName);
        Directory.CreateDirectory(packageDir);

        var bundledName = $"test.{dirName}";
        var featureFlagLine = featureFlag is not null ? $"\nfeature_flag = \"{featureFlag}\"" : "";

        // Write package.toml
        File.WriteAllText(Path.Combine(packageDir, "package.toml"), $"""
            [package]
            name = "{bundledName}"
            title = "{packageName}"{featureFlagLine}

            [contributes]
            document_editors = ["editor.document.toml"]
            """);

        var fileTypesToml = string.Join("\n", fileTypes.Select(ft => $"""
            [[document_file_types]]
            extension = "{ft.Extension}"
            display_name = "{ft.DisplayName}"
            """));

        var templatesToml = "";
        if (templates is not null)
        {
            templatesToml = string.Join("\n", templates.Select(t => $"""
                [[document_templates]]
                id = "{t.Id}"
                display_name = "{t.DisplayName}"
                template_file = "{t.File}"
                default = {t.Default.ToString().ToLower()}
                """));
        }

        File.WriteAllText(Path.Combine(packageDir, "editor.document.toml"), $"""
            [document]
            id = "{bundledName}-doc"
            type = "custom"
            entry_point = "index.html"
            display_name = "{packageName}"

            {fileTypesToml}

            {templatesToml}
            """);

        // Create localization files
        if (localizationStrings is not null)
        {
            var localizationFolder = Path.Combine(packageDir, PackageLocalizationService.LocalizationFolder);
            Directory.CreateDirectory(localizationFolder);

            var entries = localizationStrings.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\"");
            var localizationJson = $"{{ {string.Join(", ", entries)} }}";
            File.WriteAllText(Path.Combine(localizationFolder, "en.json"), localizationJson);
        }

        // Create template files
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
