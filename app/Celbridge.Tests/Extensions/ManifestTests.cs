using Celbridge.Extensions;

namespace Celbridge.Tests.Extensions;

[TestFixture]
public class ManifestTests
{
    private string _tempFolder = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ManifestTests));
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
    public void LoadExtension_ValidCustomDocument_ReturnsManifest()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.my-editor"
            name = "My Editor"
            version = "1.0.0"

            [contributes]
            documents = ["editor.document.toml"]
            """);

        WriteDocumentToml("editor.document.toml", """
            [document]
            id = "my-editor-doc"
            type = "custom"
            entry_point = "index.html"

            [[file_types]]
            extension = ".myext"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();

        var manifest = result.Value[0];
        manifest.Id.Should().Be("my-editor-doc");
        manifest.Name.Should().Be("My Editor");
        manifest.Type.Should().Be(EditorType.Custom);
        manifest.FileTypes.Should().ContainSingle().Which.Extension.Should().Be(".myext");
        manifest.EntryPoint.Should().Be("index.html");
        manifest.ExtensionDirectory.Should().Be(_tempFolder);
        manifest.HostName.Should().Be("ext-test-my-editor.celbridge");
    }

    [Test]
    public void LoadExtension_ValidCodeDocument_WithPreview_ReturnsManifest()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.code-preview"
            name = "Code Preview"
            version = "1.0.0"

            [contributes]
            documents = ["cpv.document.toml"]
            """);

        WriteDocumentToml("cpv.document.toml", """
            [document]
            id = "cpv-doc"
            type = "code"

            [[file_types]]
            extension = ".cpv"

            [monaco]
            customizations = "customize.js"

            [preview]
            asset_folder = "preview"
            page_url = "index.html"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var manifest = result.Value[0];
        manifest.Type.Should().Be(EditorType.Code);
        manifest.Preview.Should().NotBeNull();
        manifest.Preview!.HostName.Should().Be("ext-test-code-preview-preview.celbridge");
        manifest.Preview.AssetFolder.Should().Be("preview");
        manifest.Preview.PageUrl.Should().Be("https://ext-test-code-preview-preview.celbridge/index.html");
        manifest.Monaco.Should().NotBeNull();
        manifest.Monaco!.Customizations.Should().Be("customize.js");
    }

    [Test]
    public void LoadExtension_MissingExtensionId_ReturnsFailure()
    {
        WriteExtensionToml("""
            [extension]
            name = "No Id"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadExtension_MissingExtensionSection_ReturnsFailure()
    {
        WriteExtensionToml("""
            [contributes]
            documents = ["doc.document.toml"]
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadExtension_EmptyFileTypes_ReturnsNoManifests()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.empty"
            name = "Empty"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "empty-doc"
            type = "custom"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        // Document with no file_types is skipped (invalid)
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Test]
    public void LoadExtension_InvalidToml_ReturnsFailure()
    {
        var path = Path.Combine(_tempFolder, "extension.toml");
        File.WriteAllText(path, "{ not valid toml }");

        var result = ManifestLoader.LoadExtension(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadExtension_NonExistentFile_ReturnsFailure()
    {
        var path = Path.Combine(_tempFolder, "nonexistent.toml");

        var result = ManifestLoader.LoadExtension(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadExtension_DefaultPriority_IsZero()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.basic"
            name = "Basic"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "basic-doc"
            type = "code"

            [[file_types]]
            extension = ".bas"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Priority.Should().Be(0);
    }

    [Test]
    public void LoadExtension_WithPriority_UsesPriority()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.priority"
            name = "Priority"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "priority-doc"
            type = "code"
            priority = 10

            [[file_types]]
            extension = ".pri"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Priority.Should().Be(10);
    }

    [Test]
    public void LoadExtension_WithExtensionFeatureFlag_PropagatedToDocuments()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.flagged"
            name = "Flagged"
            version = "1.0.0"
            feature_flag = "my-feature"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "flagged-doc"
            type = "custom"

            [[file_types]]
            extension = ".flag"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].FeatureFlag.Should().Be("my-feature");
    }

    [Test]
    public void LoadExtension_WithoutFeatureFlag_ReturnsNull()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.noflag"
            name = "NoFlag"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "noflag-doc"
            type = "custom"

            [[file_types]]
            extension = ".nf"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].FeatureFlag.Should().BeNull();
    }

    [Test]
    public void LoadExtension_WithCapabilities_ReturnsCapabilities()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.capable"
            name = "Capable"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "capable-doc"
            type = "custom"
            capabilities = ["dialog", "input"]

            [[file_types]]
            extension = ".cap"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Capabilities.Should().HaveCount(2);
        result.Value[0].Capabilities.Should().Contain("dialog");
        result.Value[0].Capabilities.Should().Contain("input");
    }

    [Test]
    public void LoadExtension_WithoutCapabilities_ReturnsEmptyList()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.nocaps"
            name = "NoCaps"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "nocaps-doc"
            type = "custom"

            [[file_types]]
            extension = ".nc"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Capabilities.Should().BeEmpty();
    }

    [Test]
    public void LoadExtension_WithTemplates_ReturnsTemplates()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.templated"
            name = "Templated"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "templated-doc"
            type = "custom"

            [[file_types]]
            extension = ".tmpl"

            [[templates]]
            id = "empty"
            display_name = "Empty File"
            file = "templates/empty.tmpl"
            default = true

            [[templates]]
            id = "example"
            display_name = "Example File"
            file = "templates/example.tmpl"
            default = false
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Templates.Should().HaveCount(2);

        var defaultTemplate = result.Value[0].Templates[0];
        defaultTemplate.Id.Should().Be("empty");
        defaultTemplate.DisplayName.Should().Be("Empty File");
        defaultTemplate.File.Should().Be("templates/empty.tmpl");
        defaultTemplate.Default.Should().BeTrue();

        var exampleTemplate = result.Value[0].Templates[1];
        exampleTemplate.Id.Should().Be("example");
        exampleTemplate.Default.Should().BeFalse();
    }

    [Test]
    public void LoadExtension_WithoutTemplates_ReturnsEmptyList()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.notemplates"
            name = "NoTemplates"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "notemplates-doc"
            type = "custom"

            [[file_types]]
            extension = ".nt"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Templates.Should().BeEmpty();
    }

    [Test]
    public void LoadExtension_FullDocument_AllFieldsPopulated()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.full-editor"
            name = "Full Editor"
            version = "2.0.0"
            feature_flag = "full-ext"

            [contributes]
            documents = ["full.document.toml"]
            """);

        WriteDocumentToml("full.document.toml", """
            [document]
            id = "full-doc"
            type = "custom"
            entry_point = "index.html"
            priority = 5
            capabilities = ["dialog", "input"]

            [[file_types]]
            extension = ".full"
            display_name = "Full_FileType"

            [[templates]]
            id = "empty"
            display_name = "Empty"
            file = "templates/empty.full"
            default = true
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var manifest = result.Value[0];
        manifest.Id.Should().Be("full-doc");
        manifest.Name.Should().Be("Full Editor");
        manifest.Type.Should().Be(EditorType.Custom);
        manifest.FileTypes.Should().ContainSingle().Which.Extension.Should().Be(".full");
        manifest.EntryPoint.Should().Be("index.html");
        manifest.Priority.Should().Be(5);
        manifest.FeatureFlag.Should().Be("full-ext");
        manifest.Capabilities.Should().HaveCount(2);
        manifest.Templates.Should().HaveCount(1);
    }

    [Test]
    public void LoadExtension_MultipleDocuments_ReturnsAll()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.multi"
            name = "Multi"
            version = "1.0.0"

            [contributes]
            documents = ["a.document.toml", "b.document.toml"]
            """);

        WriteDocumentToml("a.document.toml", """
            [document]
            id = "doc-a"
            type = "custom"

            [[file_types]]
            extension = ".aaa"
            """);

        WriteDocumentToml("b.document.toml", """
            [document]
            id = "doc-b"
            type = "code"

            [[file_types]]
            extension = ".bbb"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be("doc-a");
        result.Value[1].Id.Should().Be("doc-b");
    }

    [Test]
    public void LoadExtension_MonacoOptions_Parsed()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.monaco"
            name = "Monaco"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "monaco-doc"
            type = "code"

            [[file_types]]
            extension = ".mon"

            [monaco]
            word_wrap = true
            scroll_beyond_last_line = false
            minimap_enabled = true
            customizations = "custom.js"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var monaco = result.Value[0].Monaco;
        monaco.Should().NotBeNull();
        monaco!.WordWrap.Should().BeTrue();
        monaco.ScrollBeyondLastLine.Should().BeFalse();
        monaco.MinimapEnabled.Should().BeTrue();
        monaco.Customizations.Should().Be("custom.js");
    }

    [Test]
    public void LoadExtension_MissingDocumentFile_SkipsDocument()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.missing-doc"
            name = "MissingDoc"
            version = "1.0.0"

            [contributes]
            documents = ["nonexistent.document.toml"]
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private void WriteExtensionToml(string content)
    {
        File.WriteAllText(Path.Combine(_tempFolder, "extension.toml"), content);
    }

    private void WriteDocumentToml(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_tempFolder, fileName), content);
    }
}
