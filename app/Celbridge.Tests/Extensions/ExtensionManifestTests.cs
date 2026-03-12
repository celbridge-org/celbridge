using Celbridge.Extensions;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class ExtensionManifestTests
{
    private string _tempFolder = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ExtensionManifestTests));
        Directory.CreateDirectory(_tempFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public void Parse_ValidCustomManifest_ReturnsManifest()
    {
        var json = """
            {
                "name": "My Editor",
                "type": "custom",
                "file_types": [{ "extension": ".myext" }],
                "entryPoint": "index.html"
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("My Editor");
        result.Value.Type.Should().Be(ExtensionEditorType.Custom);
        result.Value.FileTypes.Should().ContainSingle().Which.Extension.Should().Be(".myext");
        result.Value.EntryPoint.Should().Be("index.html");
        result.Value.ExtensionDirectory.Should().Be(_tempFolder);
        result.Value.HostName.Should().Be("ext-my-editor.celbridge");
    }

    [Test]
    public void Parse_ValidCodeManifest_WithPreview_ReturnsManifest()
    {
        var json = """
            {
                "name": "Code Preview",
                "type": "code",
                "file_types": [{ "extension": ".cpv" }],
                "preview": {
                    "hostName": "cpv-preview.celbridge",
                    "assetFolder": "preview",
                    "pageUrl": "https://cpv-preview.celbridge/index.html"
                },
                "customizations": "customize.js"
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Type.Should().Be(ExtensionEditorType.Code);
        result.Value.Preview.Should().NotBeNull();
        result.Value.Preview!.HostName.Should().Be("cpv-preview.celbridge");
        result.Value.Preview.AssetFolder.Should().Be("preview");
        result.Value.Customizations.Should().Be("customize.js");
    }

    [Test]
    public void Parse_MissingName_ReturnsFailure()
    {
        var json = """
            {
                "type": "custom",
                "file_types": [{ "extension": ".myext" }]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Parse_EmptyFileTypes_ReturnsFailure()
    {
        var json = """
            {
                "name": "Empty",
                "type": "custom",
                "file_types": []
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Parse_InvalidJson_ReturnsFailure()
    {
        var path = Path.Combine(_tempFolder, "editor.json");
        File.WriteAllText(path, "{ not valid json }");

        var result = ExtensionManifest.Parse(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Parse_NonExistentFile_ReturnsFailure()
    {
        var path = Path.Combine(_tempFolder, "nonexistent.json");

        var result = ExtensionManifest.Parse(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Parse_DefaultPriority_IsZero()
    {
        var json = """
            {
                "name": "Basic",
                "type": "code",
                "file_types": [{ "extension": ".bas" }]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Priority.Should().Be(0);
    }

    [Test]
    public void Parse_WithPriority_UsesPriority()
    {
        var json = """
            {
                "name": "Priority",
                "type": "code",
                "file_types": [{ "extension": ".pri" }],
                "priority": 10
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Priority.Should().Be(10);
    }

    [Test]
    public void Parse_CommentsAndTrailingCommas_AreAllowed()
    {
        var json = """
            {
                // This is a comment
                "name": "Relaxed",
                "type": "code",
                "file_types": [{ "extension": ".rlx" }],
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Relaxed");
    }

    [Test]
    public void Parse_WithFeatureFlag_ReturnsFeatureFlag()
    {
        var json = """
            {
                "name": "Flagged",
                "type": "custom",
                "file_types": [{ "extension": ".flag" }],
                "featureFlag": "my-feature"
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.FeatureFlag.Should().Be("my-feature");
    }

    [Test]
    public void Parse_WithoutFeatureFlag_ReturnsNull()
    {
        var json = """
            {
                "name": "NoFlag",
                "type": "custom",
                "file_types": [{ "extension": ".nf" }]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.FeatureFlag.Should().BeNull();
    }

    [Test]
    public void Parse_WithCapabilities_ReturnsCapabilities()
    {
        var json = """
            {
                "name": "Capable",
                "type": "custom",
                "file_types": [{ "extension": ".cap" }],
                "capabilities": ["dialog", "input"]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Capabilities.Should().HaveCount(2);
        result.Value.Capabilities.Should().Contain("dialog");
        result.Value.Capabilities.Should().Contain("input");
    }

    [Test]
    public void Parse_WithoutCapabilities_ReturnsEmptyList()
    {
        var json = """
            {
                "name": "NoCaps",
                "type": "custom",
                "file_types": [{ "extension": ".nc" }]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Capabilities.Should().BeEmpty();
    }

    [Test]
    public void Parse_WithTemplates_ReturnsTemplates()
    {
        var json = """
            {
                "name": "Templated",
                "type": "custom",
                "file_types": [{ "extension": ".tmpl" }],
                "templates": [
                    {
                        "id": "empty",
                        "displayName": "Empty File",
                        "file": "templates/empty.tmpl",
                        "default": true
                    },
                    {
                        "id": "example",
                        "displayName": "Example File",
                        "file": "templates/example.tmpl",
                        "default": false
                    }
                ]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Templates.Should().HaveCount(2);

        var defaultTemplate = result.Value.Templates[0];
        defaultTemplate.Id.Should().Be("empty");
        defaultTemplate.DisplayName.Should().Be("Empty File");
        defaultTemplate.File.Should().Be("templates/empty.tmpl");
        defaultTemplate.Default.Should().BeTrue();

        var exampleTemplate = result.Value.Templates[1];
        exampleTemplate.Id.Should().Be("example");
        exampleTemplate.Default.Should().BeFalse();
    }

    [Test]
    public void Parse_WithoutTemplates_ReturnsEmptyList()
    {
        var json = """
            {
                "name": "NoTemplates",
                "type": "custom",
                "file_types": [{ "extension": ".nt" }]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Templates.Should().BeEmpty();
    }

    [Test]
    public void Parse_FullManifest_AllFieldsPopulated()
    {
        var json = """
            {
                "name": "Full Editor",
                "type": "custom",
                "file_types": [{ "extension": ".full", "displayName": "Full_FileType" }],
                "entryPoint": "index.html",
                "priority": 5,
                "featureFlag": "full-editor",
                "capabilities": ["dialog", "input"],
                "templates": [
                    {
                        "id": "empty",
                        "displayName": "Empty",
                        "file": "templates/empty.full",
                        "default": true
                    }
                ]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        var manifest = result.Value;
        manifest.Name.Should().Be("Full Editor");
        manifest.Type.Should().Be(ExtensionEditorType.Custom);
        manifest.FileTypes.Should().ContainSingle().Which.Extension.Should().Be(".full");
        manifest.EntryPoint.Should().Be("index.html");
        manifest.Priority.Should().Be(5);
        manifest.FeatureFlag.Should().Be("full-editor");
        manifest.Capabilities.Should().HaveCount(2);
        manifest.Templates.Should().HaveCount(1);
    }

    private string WriteManifest(string json)
    {
        var path = Path.Combine(_tempFolder, "editor.json");
        File.WriteAllText(path, json);
        return path;
    }
}
