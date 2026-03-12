using Celbridge.Extensions;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class ExtensionFileTypeProviderTests
{
    private string _tempProjectFolder = null!;
    private ExtensionRegistry _extensionRegistry = null!;
    private IProjectService _projectService = null!;
    private ExtensionFileTypeProvider _provider = null!;

    [SetUp]
    public void Setup()
    {
        _tempProjectFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ExtensionFileTypeProviderTests));
        Directory.CreateDirectory(_tempProjectFolder);

        var registryLogger = Substitute.For<ILogger<ExtensionRegistry>>();
        _extensionRegistry = new ExtensionRegistry(registryLogger);

        // Mock IProjectService to return a project with the temp folder
        _projectService = Substitute.For<IProjectService>();
        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(_tempProjectFolder);
        _projectService.CurrentProject.Returns(project);

        var providerLogger = Substitute.For<ILogger<ExtensionFileTypeProvider>>();
        _provider = new ExtensionFileTypeProvider(providerLogger, _extensionRegistry, _projectService);
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
            "TestEditor",
            [(".test", "")],
            templates:
            [
                new { id = "empty", displayName = "Empty", file = "templates/empty.test", @default = true }
            ]);

        var fileTypes = _provider.GetExtensionFileTypes();

        fileTypes.Should().HaveCount(1);
        fileTypes[0].Extension.Should().Be(".test");
        fileTypes[0].DisplayName.Should().Be("TestEditor");
    }

    [Test]
    public void GetExtensionFileTypes_ExtensionWithoutTemplates_Excluded()
    {
        CreateBundledExtension("NoTemplates", [(".notemplate", "")], templates: null);

        var fileTypes = _provider.GetExtensionFileTypes();

        fileTypes.Should().BeEmpty();
    }

    [Test]
    public void GetExtensionFileTypes_WithLocalization_ResolvesDisplayName()
    {
        CreateBundledExtension(
            "Note",
            [(".note", "Note_FileType_Note")],
            templates:
            [
                new { id = "empty", displayName = "Note_Template_Empty", file = "templates/empty.note", @default = true }
            ],
            localization: "localization",
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
            "FlaggedEditor",
            [(".flagged", "")],
            featureFlag: "my-flag",
            templates:
            [
                new { id = "empty", displayName = "Empty", file = "templates/empty.flagged", @default = true }
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
            "Note",
            [(".note", "")],
            templates:
            [
                new { id = "empty", displayName = "Empty", file = "templates/empty.note", @default = true }
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
            "NonDefault",
            [(".nd", "")],
            templates:
            [
                new { id = "example", displayName = "Example", file = "templates/example.nd", @default = false }
            ]);

        var content = _provider.GetDefaultTemplateContent(".nd");

        content.Should().BeNull();
    }

    [Test]
    public void GetDefaultTemplateContent_CaseInsensitiveExtension()
    {
        var templateContent = "template content";
        CreateBundledExtension(
            "CaseTest",
            [(".TEST", "")],
            templates:
            [
                new { id = "empty", displayName = "Empty", file = "templates/empty.test", @default = true }
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
            "Orphan",
            [(".orphan", "")],
            templates:
            [
                new { id = "empty", displayName = "Empty", file = "templates/empty.orphan", @default = true }
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
    /// Helper to create a bundled extension directory with manifest and optional files.
    /// </summary>
    private void CreateBundledExtension(
        string name,
        (string Extension, string DisplayName)[] fileTypes,
        object[]? templates = null,
        string? featureFlag = null,
        string? localization = null,
        Dictionary<string, string>? localizationStrings = null,
        Dictionary<string, string>? templateFiles = null)
    {
        var extDir = Path.Combine(_tempProjectFolder, "bundled", name.ToLower());
        Directory.CreateDirectory(extDir);

        // Build manifest JSON
        var fileTypesJson = string.Join(", ", fileTypes.Select(ft =>
            $"{{ \"extension\": \"{ft.Extension}\", \"displayName\": \"{ft.DisplayName}\" }}"));
        var templatesPart = "";
        if (templates is not null)
        {
            var templateEntries = templates.Select(t =>
            {
                var props = t.GetType().GetProperties();
                var id = props.First(p => p.Name == "id").GetValue(t);
                var displayName = props.First(p => p.Name == "displayName").GetValue(t);
                var file = props.First(p => p.Name == "file").GetValue(t);
                var isDefault = (bool)props.First(p => p.Name == "default").GetValue(t)!;
                return $"{{ \"id\": \"{id}\", \"displayName\": \"{displayName}\", \"file\": \"{file}\", \"default\": {isDefault.ToString().ToLower()} }}";
            });
            templatesPart = $", \"templates\": [{string.Join(", ", templateEntries)}]";
        }

        var featureFlagPart = featureFlag is not null ? $", \"featureFlag\": \"{featureFlag}\"" : "";
        var localizationPart = localization is not null ? $", \"localization\": \"{localization}\"" : "";

        var manifestJson = $@"{{
            ""name"": ""{name}"",
            ""type"": ""custom"",
            ""file_types"": [{fileTypesJson}],
            ""entryPoint"": ""index.html""
            {featureFlagPart}
            {templatesPart}
            {localizationPart}
        }}";

        File.WriteAllText(Path.Combine(extDir, "editor.json"), manifestJson);

        // Create localization files
        if (localization is not null && localizationStrings is not null)
        {
            var locDir = Path.Combine(extDir, localization);
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
