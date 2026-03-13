using Celbridge.Extensions;
using Celbridge.Logging;
using Celbridge.Projects;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class FileTypeProviderTests
{
    private string _tempProjectFolder = null!;
    private ExtensionRegistry _extensionRegistry = null!;
    private IProjectService _projectService = null!;
    private FileTypeProvider _provider = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FileTypeProviderTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var registryLogger = Substitute.For<ILogger<ExtensionRegistry>>();
        _extensionRegistry = new ExtensionRegistry(registryLogger);

        // Mock IProjectService to return a project with the temp folder
        _projectService = Substitute.For<IProjectService>();
        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(_tempProjectFolder);
        _projectService.CurrentProject.Returns(project);

        var providerLogger = Substitute.For<ILogger<FileTypeProvider>>();
        _provider = new FileTypeProvider(providerLogger, _extensionRegistry, _projectService);
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
    public void GetExtensionFileTypes_NoExtensions_ReturnsEmpty()
    {
        var fileTypes = _provider.GetExtensionFileTypes();

        fileTypes.Should().BeEmpty();
    }

    [Test]
    public void GetExtensionFileTypes_ExtensionWithTemplates_ReturnsFileType()
    {
        CreateBundledExtension(
            "test-editor",
            "TestEditor",
            [(".test", "")],
            templates:
            [
                ("empty", "Empty", "templates/empty.test", true)
            ]);

        var fileTypes = _provider.GetExtensionFileTypes();

        fileTypes.Should().HaveCount(1);
        fileTypes[0].Extension.Should().Be(".test");
        fileTypes[0].DisplayName.Should().Be("TestEditor");
    }

    [Test]
    public void GetExtensionFileTypes_ExtensionWithoutTemplates_Excluded()
    {
        CreateBundledExtension("no-templates", "NoTemplates", [(".notemplate", "")], templates: null);

        var fileTypes = _provider.GetExtensionFileTypes();

        fileTypes.Should().BeEmpty();
    }

    [Test]
    public void GetExtensionFileTypes_WithLocalization_ResolvesDisplayName()
    {
        CreateBundledExtension(
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

        var fileTypes = _provider.GetExtensionFileTypes();

        fileTypes.Should().HaveCount(1);
        fileTypes[0].DisplayName.Should().Be("My Localized Note");
    }

    [Test]
    public void GetExtensionFileTypes_FeatureFlagPopulated()
    {
        CreateBundledExtension(
            "flagged-editor",
            "FlaggedEditor",
            [(".flagged", "")],
            featureFlag: "my-flag",
            templates:
            [
                ("empty", "Empty", "templates/empty.flagged", true)
            ]);

        var fileTypes = _provider.GetExtensionFileTypes();

        fileTypes.Should().HaveCount(1);
        fileTypes[0].FeatureFlag.Should().Be("my-flag");
    }

    [Test]
    public void GetDefaultTemplateContent_ExtensionWithTemplate_ReturnsContent()
    {
        var templateContent = "{\"type\":\"doc\"}";
        CreateBundledExtension(
            "note",
            "Note",
            [(".note", "")],
            templates:
            [
                ("empty", "Empty", "templates/empty.note", true)
            ],
            templateFiles: new Dictionary<string, string>
            {
                ["templates/empty.note"] = templateContent
            });

        var content = _provider.GetDefaultTemplateContent(".note");

        content.Should().NotBeNull();
        var text = System.Text.Encoding.UTF8.GetString(content!);
        text.Should().Be(templateContent);
    }

    [Test]
    public void GetDefaultTemplateContent_NoMatchingExtension_ReturnsNull()
    {
        var content = _provider.GetDefaultTemplateContent(".unknown");

        content.Should().BeNull();
    }

    [Test]
    public void GetDefaultTemplateContent_ExtensionWithoutDefaultTemplate_ReturnsNull()
    {
        CreateBundledExtension(
            "non-default",
            "NonDefault",
            [(".nd", "")],
            templates:
            [
                ("example", "Example", "templates/example.nd", false)
            ]);

        var content = _provider.GetDefaultTemplateContent(".nd");

        content.Should().BeNull();
    }

    [Test]
    public void GetDefaultTemplateContent_CaseInsensitiveExtension()
    {
        var templateContent = "template content";
        CreateBundledExtension(
            "case-test",
            "CaseTest",
            [(".TEST", "")],
            templates:
            [
                ("empty", "Empty", "templates/empty.test", true)
            ],
            templateFiles: new Dictionary<string, string>
            {
                ["templates/empty.test"] = templateContent
            });

        var content = _provider.GetDefaultTemplateContent(".test");

        content.Should().NotBeNull();
    }

    [Test]
    public void GetDefaultTemplateContent_NoProject_ReturnsNull()
    {
        _projectService.CurrentProject.Returns((IProject?)null);

        CreateBundledExtension(
            "orphan",
            "Orphan",
            [(".orphan", "")],
            templates:
            [
                ("empty", "Empty", "templates/empty.orphan", true)
            ],
            templateFiles: new Dictionary<string, string>
            {
                ["templates/empty.orphan"] = "content"
            });

        // Should still find bundled extensions even without a project
        var content = _provider.GetDefaultTemplateContent(".orphan");
        content.Should().NotBeNull();
    }

    /// <summary>
    /// Helper to create a bundled extension directory with TOML manifests and optional files.
    /// </summary>
    private void CreateBundledExtension(
        string dirName,
        string extensionName,
        (string Extension, string DisplayName)[] fileTypes,
        (string Id, string DisplayName, string File, bool Default)[]? templates = null,
        string? featureFlag = null,
        Dictionary<string, string>? localizationStrings = null,
        Dictionary<string, string>? templateFiles = null)
    {
        var extDir = Path.Combine(_tempProjectFolder, "bundled", dirName);
        Directory.CreateDirectory(extDir);

        var extId = $"test.{dirName}";
        var featureFlagLine = featureFlag is not null ? $"\nfeature_flag = \"{featureFlag}\"" : "";

        // Write extension.toml
        File.WriteAllText(Path.Combine(extDir, "extension.toml"), $"""
            [extension]
            id = "{extId}"
            name = "{extensionName}"
            version = "1.0.0"{featureFlagLine}

            [contributes]
            documents = ["editor.document.toml"]
            """);

        // Build document TOML
        var fileTypesToml = string.Join("\n", fileTypes.Select(ft =>
        {
            var displayNameLine = !string.IsNullOrEmpty(ft.DisplayName)
                ? $"\ndisplay_name = \"{ft.DisplayName}\""
                : "";
            return $"""
                [[document_file_types]]
                extension = "{ft.Extension}"{displayNameLine}
                """;
        }));

        var templatesToml = "";
        if (templates is not null)
        {
            templatesToml = string.Join("\n", templates.Select(t => $"""
                [[document_templates]]
                id = "{t.Id}"
                display_name = "{t.DisplayName}"
                file = "{t.File}"
                default = {t.Default.ToString().ToLower()}
                """));
        }

        File.WriteAllText(Path.Combine(extDir, "editor.document.toml"), $"""
            [document]
            id = "{extId}-doc"
            type = "custom"
            entry_point = "index.html"

            {fileTypesToml}

            {templatesToml}
            """);

        // Create localization files
        if (localizationStrings is not null)
        {
            var locDir = Path.Combine(extDir, LocalizationHelper.LocalizationFolder);
            Directory.CreateDirectory(locDir);

            var entries = localizationStrings.Select(kv => $"\"{kv.Key}\": \"{kv.Value}\"");
            var locJson = $"{{ {string.Join(", ", entries)} }}";
            File.WriteAllText(Path.Combine(locDir, "en.json"), locJson);
        }

        // Create template files
        if (templateFiles is not null)
        {
            foreach (var kvp in templateFiles)
            {
                var filePath = Path.Combine(extDir, kvp.Key);
                var dir = Path.GetDirectoryName(filePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, kvp.Value);
            }
        }

        _extensionRegistry.RegisterBundledExtensionPath(extDir);
    }
}
