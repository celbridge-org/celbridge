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
            document_editors = ["editor.document.toml"]
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

        var extension = result.Value;
        extension.Info.Name.Should().Be("My Editor");
        extension.Info.ExtensionFolder.Should().Be(_tempFolder);
        extension.Info.HostName.Should().Be("ext-test-my-editor.celbridge");
        extension.DocumentEditors.Should().ContainSingle();

        var contribution = extension.DocumentEditors[0];
        contribution.Should().BeOfType<CustomDocumentContribution>();
        contribution.Id.Should().Be("my-editor-doc");
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".myext");
        ((CustomDocumentContribution)contribution).EntryPoint.Should().Be("index.html");
        contribution.Extension.ExtensionFolder.Should().Be(_tempFolder);
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
            document_editors = ["cpv.document.toml"]
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
            entry_point = "preview/index.html"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var contribution = result.Value.DocumentEditors[0];
        contribution.Should().BeOfType<CodeDocumentContribution>();

        var codeContribution = (CodeDocumentContribution)contribution;
        codeContribution.CodePreview.Should().NotBeNull();
        codeContribution.CodePreview!.EntryPoint.Should().Be("preview/index.html");
        codeContribution.CodeEditor.Should().NotBeNull();
        codeContribution.CodeEditor!.CustomizationScript.Should().Be("customize.js");
    }

    [Test]
    public void LoadExtension_MissingExtensionId_ReturnsFailure()
    {
        WriteExtensionToml("""
            [extension]
            name = "No Id"
            version = "1.0.0"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadExtension_MissingExtensionSection_ReturnsFailure()
    {
        WriteExtensionToml("""
            [contributes]
            document_editors = ["doc.document.toml"]
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
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "empty-doc"
            type = "custom"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        // Document with no document_file_types is skipped (invalid)
        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
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
            document_editors = ["doc.document.toml"]
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
        result.Value.DocumentEditors[0].Priority.Should().Be(EditorPriority.Default);
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
            document_editors = ["doc.document.toml"]
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
        result.Value.DocumentEditors[0].Priority.Should().Be(EditorPriority.Option);
    }

    [Test]
    public void LoadExtension_WithExtensionFeatureFlag_PropagatedToInfo()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.flagged"
            name = "Flagged"
            version = "1.0.0"
            feature_flag = "my-feature"

            [contributes]
            document_editors = ["doc.document.toml"]
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
        result.Value.Info.FeatureFlag.Should().Be("my-feature");
        result.Value.DocumentEditors[0].Extension.FeatureFlag.Should().Be("my-feature");
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
            document_editors = ["doc.document.toml"]
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
        result.Value.Info.FeatureFlag.Should().BeNull();
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
            document_editors = ["doc.document.toml"]
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
        result.Value.DocumentEditors[0].Templates.Should().HaveCount(2);

        var defaultTemplate = result.Value.DocumentEditors[0].Templates[0];
        defaultTemplate.Id.Should().Be("empty");
        defaultTemplate.DisplayName.Should().Be("Empty File");
        defaultTemplate.File.Should().Be("templates/empty.tmpl");
        defaultTemplate.Default.Should().BeTrue();

        var exampleTemplate = result.Value.DocumentEditors[0].Templates[1];
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
            document_editors = ["doc.document.toml"]
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
        result.Value.DocumentEditors[0].Templates.Should().BeEmpty();
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
            document_editors = ["full.document.toml"]
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

        var extension = result.Value;
        extension.Info.Name.Should().Be("Full Editor");
        extension.Info.FeatureFlag.Should().Be("full-ext");

        var contribution = extension.DocumentEditors[0];
        contribution.Should().BeOfType<CustomDocumentContribution>();
        contribution.Id.Should().Be("full-doc");
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".full");
        ((CustomDocumentContribution)contribution).EntryPoint.Should().Be("index.html");
        contribution.Priority.Should().Be(EditorPriority.Default);
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
            document_editors = ["a.document.toml", "b.document.toml"]
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
        result.Value.DocumentEditors.Should().HaveCount(2);
        result.Value.DocumentEditors[0].Should().BeOfType<CustomDocumentContribution>();
        result.Value.DocumentEditors[0].Id.Should().Be("doc-a");
        result.Value.DocumentEditors[1].Should().BeOfType<CodeDocumentContribution>();
        result.Value.DocumentEditors[1].Id.Should().Be("doc-b");
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
            document_editors = ["doc.document.toml"]
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
        var codeContribution = result.Value.DocumentEditors[0] as CodeDocumentContribution;
        codeContribution.Should().NotBeNull();
        codeContribution!.CodeEditor.Should().NotBeNull();
        codeContribution.CodeEditor!.WordWrap.Should().BeTrue();
        codeContribution.CodeEditor.ScrollBeyondLastLine.Should().BeFalse();
        codeContribution.CodeEditor.MinimapEnabled.Should().BeTrue();
        codeContribution.CodeEditor.CustomizationScript.Should().Be("custom.js");
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
            document_editors = ["nonexistent.document.toml"]
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
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

            [contributes]
            document_editors = ["a.document.toml", "b.document.toml"]
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
        result.Value.DocumentEditors.Should().HaveCount(2);

        // Both contributions share the same ExtensionInfo via the parent FileExtension
        result.Value.Info.Name.Should().Be("Shared");
        result.Value.Info.FeatureFlag.Should().Be("shared-flag");
        result.Value.DocumentEditors[0].Extension.Name.Should().Be("Shared");
        result.Value.DocumentEditors[1].Extension.Name.Should().Be("Shared");
    }

    [Test]
    public void LoadExtension_CustomDocumentWithoutEntryPoint_DefaultsToIndexHtml()
    {
        WriteExtensionToml("""
            [extension]
            id = "test.no-entry"
            name = "NoEntry"
            version = "1.0.0"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "no-entry-doc"
            type = "custom"

            [[document_file_types]]
            extension = ".ne"
            """);

        var result = ManifestLoader.LoadExtension(Path.Combine(_tempFolder, "extension.toml"));

        result.IsSuccess.Should().BeTrue();
        var customContribution = result.Value.DocumentEditors[0] as CustomDocumentContribution;
        customContribution.Should().NotBeNull();
        customContribution!.EntryPoint.Should().Be("index.html");
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
