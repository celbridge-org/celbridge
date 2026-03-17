using Celbridge.Extensions;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Settings;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class ExtensionServiceDocumentTypeTests
{
    private string _tempProjectFolder = null!;
    private ExtensionService _service = null!;
    private IModuleService _moduleService = null!;
    private IFeatureFlags _featureFlags = null!;
    private List<string> _bundledExtensionPaths = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ExtensionServiceDocumentTypeTests));
        Directory.CreateDirectory(_tempProjectFolder);

        _bundledExtensionPaths = [];
        _moduleService = Substitute.For<IModuleService>();
        _moduleService.GetExtensionFolders().Returns(_ => _bundledExtensionPaths);

        _featureFlags = Substitute.For<IFeatureFlags>();
        _featureFlags.IsEnabled(Arg.Any<string>()).Returns(true);

        var logger = Substitute.For<ILogger<ExtensionService>>();
        var messengerService = Substitute.For<IMessengerService>();
        var localizationLogger = Substitute.For<ILogger<ExtensionLocalizationService>>();
        var localizationService = new ExtensionLocalizationService(localizationLogger);
        _service = new ExtensionService(logger, _moduleService, messengerService, _featureFlags, localizationService);
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
    public void GetDocumentTypes_NoExtensions_ReturnsEmpty()
    {
        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().BeEmpty();
    }

    [Test]
    public void GetDocumentTypes_ExtensionWithTemplates_ReturnsDocumentType()
    {
        CreateBundledExtension(
            "test-editor",
            "TestEditor",
            [(".test", "")],
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
    public void GetDocumentTypes_ExtensionWithoutTemplates_Excluded()
    {
        CreateBundledExtension("no-templates", "NoTemplates", [(".notemplate", "")], templates: null);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().BeEmpty();
    }

    [Test]
    public void GetDocumentTypes_WithLocalization_ResolvesDisplayName()
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

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().HaveCount(1);
        documentTypes[0].DisplayName.Should().Be("My Localized Note");
    }

    [Test]
    public void GetDocumentTypes_DisabledFeatureFlag_Excluded()
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

        _featureFlags.IsEnabled("my-flag").Returns(false);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().BeEmpty();
    }

    [Test]
    public void GetDocumentTypes_EnabledFeatureFlag_Included()
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

        _featureFlags.IsEnabled("my-flag").Returns(true);

        var documentTypes = _service.GetDocumentTypes();

        documentTypes.Should().HaveCount(1);
    }

    [Test]
    public void GetDocumentTypes_MultipleFileExtensions_AllIncluded()
    {
        CreateBundledExtension(
            "multi-ext",
            "MultiExt",
            [(".md", ""), (".markdown", "")],
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

        var content = _service.GetDefaultTemplateContent(".nd");

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

        var content = _service.GetDefaultTemplateContent(".test");

        content.Should().NotBeNull();
    }

    [Test]
    public void GetDefaultTemplateContent_NoProject_StillFindsBundled()
    {
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
        var content = _service.GetDefaultTemplateContent(".orphan");
        content.Should().NotBeNull();
    }

    /// <summary>
    /// Helper to create a bundled extension directory with TOML manifests and optional files.
    /// Registers the path with the module service mock and re-discovers extensions.
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
            document_editors = ["editor.document.toml"]
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
                template_file = "{t.File}"
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
            var localizationFolder = Path.Combine(extDir, ExtensionLocalizationService.LocalizationFolder);
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
                var filePath = Path.Combine(extDir, kvp.Key);
                var directory = Path.GetDirectoryName(filePath)!;
                Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, kvp.Value);
            }
        }

        _bundledExtensionPaths.Add(extDir);
        _service.Initialize(_tempProjectFolder);
    }
}
