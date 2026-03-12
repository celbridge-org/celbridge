using Celbridge.Documents.Extensions;

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
                "extensions": [".myext"],
                "entryPoint": "index.html"
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("My Editor");
        result.Value.Type.Should().Be(ExtensionEditorType.Custom);
        result.Value.Extensions.Should().ContainSingle().Which.Should().Be(".myext");
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
                "extensions": [".cpv"],
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
                "extensions": [".myext"]
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void Parse_EmptyExtensions_ReturnsFailure()
    {
        var json = """
            {
                "name": "Empty",
                "type": "custom",
                "extensions": []
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
                "extensions": [".bas"]
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
                "extensions": [".pri"],
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
                "extensions": [".rlx",],
            }
            """;
        var path = WriteManifest(json);

        var result = ExtensionManifest.Parse(path);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Relaxed");
    }

    private string WriteManifest(string json)
    {
        var path = Path.Combine(_tempFolder, "editor.json");
        File.WriteAllText(path, json);
        return path;
    }
}
