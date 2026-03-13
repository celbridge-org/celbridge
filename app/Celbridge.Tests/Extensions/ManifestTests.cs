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
    public void LoadExtension_ValidCustomDocument_ReturnsContribution()
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

            [[document_file_types]]
            extension = ".myext"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();

        var contribution = result.Value[0];
        contribution.Id.Should().Be("my-editor-doc");
        contribution.Extension.Name.Should().Be("My Editor");
        contribution.Type.Should().Be(DocumentEditorType.Custom);
        contribution.FileTypes.Should().ContainSingle().Which.Extension.Should().Be(".myext");
        contribution.EntryPoint.Should().Be("index.html");
        contribution.Extension.ExtensionDirectory.Should().Be(_tempFolder);
        contribution.Extension.HostName.Should().Be("ext-test-my-editor.celbridge");
    }

    [Test]
    public void LoadExtension_ValidCodeDocument_WithPreview_ReturnsContribution()
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

            [[document_file_types]]
            extension = ".cpv"

            [code_editor]
            customizations = "customize.js"

            [code_preview]
            asset_folder = "preview"
            page_url = "index.html"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var contribution = result.Value[0];
        contribution.Type.Should().Be(DocumentEditorType.Code);
        contribution.CodePreview.Should().NotBeNull();
        contribution.CodePreview!.HostName.Should().Be("ext-test-code-preview-preview.celbridge");
        contribution.CodePreview.AssetFolder.Should().Be("preview");
        contribution.CodePreview.PageUrl.Should().Be("https://ext-test-code-preview-preview.celbridge/index.html");
        contribution.CodeEditor.Should().NotBeNull();
        contribution.CodeEditor!.Customizations.Should().Be("customize.js");
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
    public void LoadExtension_EmptyFileTypes_ReturnsNoContributions()
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

        // Document with no document_file_types is skipped (invalid)
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
    public void LoadExtension_DefaultPriority_IsDefault()
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

            [[document_file_types]]
            extension = ".bas"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Priority.Should().Be(EditorPriority.Default);
    }

    [Test]
    public void LoadExtension_WithOptionPriority_UsesOption()
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
            priority = "option"

            [[document_file_types]]
            extension = ".pri"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Priority.Should().Be(EditorPriority.Option);
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

            [[document_file_types]]
            extension = ".flag"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Extension.FeatureFlag.Should().Be("my-feature");
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

            [[document_file_types]]
            extension = ".nf"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Extension.FeatureFlag.Should().BeNull();
    }

    [Test]
    public void LoadExtension_WithCapabilities_ReturnsCapabilities()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.capable"
            name = "Capable"
            version = "1.0.0"
            capabilities = ["dialog", "input"]

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "capable-doc"
            type = "custom"

            [[document_file_types]]
            extension = ".cap"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Extension.Capabilities.Should().HaveCount(2);
        result.Value[0].Extension.Capabilities.Should().Contain("dialog");
        result.Value[0].Extension.Capabilities.Should().Contain("input");
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

            [[document_file_types]]
            extension = ".nc"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].Extension.Capabilities.Should().BeEmpty();
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

            [[document_file_types]]
            extension = ".tmpl"

            [[document_templates]]
            id = "empty"
            display_name = "Empty File"
            file = "templates/empty.tmpl"
            default = true

            [[document_templates]]
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

            [[document_file_types]]
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
            capabilities = ["dialog", "input"]

            [contributes]
            documents = ["full.document.toml"]
            """);

        WriteDocumentToml("full.document.toml", """
            [document]
            id = "full-doc"
            type = "custom"
            entry_point = "index.html"
            priority = "default"

            [[document_file_types]]
            extension = ".full"
            display_name = "Full_FileType"

            [[document_templates]]
            id = "empty"
            display_name = "Empty"
            file = "templates/empty.full"
            default = true
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var contribution = result.Value[0];
        contribution.Id.Should().Be("full-doc");
        contribution.Extension.Name.Should().Be("Full Editor");
        contribution.Type.Should().Be(DocumentEditorType.Custom);
        contribution.FileTypes.Should().ContainSingle().Which.Extension.Should().Be(".full");
        contribution.EntryPoint.Should().Be("index.html");
        contribution.Priority.Should().Be(EditorPriority.Default);
        contribution.Extension.FeatureFlag.Should().Be("full-ext");
        contribution.Extension.Capabilities.Should().HaveCount(2);
        contribution.Templates.Should().HaveCount(1);
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

            [[document_file_types]]
            extension = ".aaa"
            """);

        WriteDocumentToml("b.document.toml", """
            [document]
            id = "doc-b"
            type = "code"

            [[document_file_types]]
            extension = ".bbb"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].Id.Should().Be("doc-a");
        result.Value[1].Id.Should().Be("doc-b");
    }

    [Test]
    public void LoadExtension_CodeEditorOptions_Parsed()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.code-editor"
            name = "CodeEditor"
            version = "1.0.0"

            [contributes]
            documents = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "code-editor-doc"
            type = "code"

            [[document_file_types]]
            extension = ".mon"

            [code_editor]
            word_wrap = true
            scroll_beyond_last_line = false
            minimap_enabled = true
            customizations = "custom.js"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var codeEditor = result.Value[0].CodeEditor;
        codeEditor.Should().NotBeNull();
        codeEditor!.WordWrap.Should().BeTrue();
        codeEditor.ScrollBeyondLastLine.Should().BeFalse();
        codeEditor.MinimapEnabled.Should().BeTrue();
        codeEditor.Customizations.Should().Be("custom.js");
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

    [Test]
    public void LoadExtension_MultipleDocuments_ShareExtensionInfo()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.shared"
            name = "Shared"
            version = "1.0.0"
            feature_flag = "shared-flag"
            capabilities = ["dialog"]

            [contributes]
            documents = ["a.document.toml", "b.document.toml"]
            """);

        WriteDocumentToml("a.document.toml", """
            [document]
            id = "doc-a"
            type = "custom"

            [[document_file_types]]
            extension = ".aaa"
            """);

        WriteDocumentToml("b.document.toml", """
            [document]
            id = "doc-b"
            type = "code"

            [[document_file_types]]
            extension = ".bbb"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        // Both contributions share the same ExtensionInfo
        result.Value[0].Extension.Name.Should().Be("Shared");
        result.Value[1].Extension.Name.Should().Be("Shared");
        result.Value[0].Extension.FeatureFlag.Should().Be("shared-flag");
        result.Value[1].Extension.FeatureFlag.Should().Be("shared-flag");
        result.Value[0].Extension.Capabilities.Should().Contain("dialog");
        result.Value[1].Extension.Capabilities.Should().Contain("dialog");
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
