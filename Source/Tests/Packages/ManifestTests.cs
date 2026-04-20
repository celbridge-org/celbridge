using Celbridge.Packages;

namespace Celbridge.Tests.Packages;

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
    public void LoadPackage_ValidCustomDocument_ReturnsContribution()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();

        var package = result.Value;
        package.Info.Name.Should().Be("My Editor");
        package.Info.PackageFolder.Should().Be(_tempFolder);
        package.Info.HostName.Should().Be("pkg-test-my-editor.celbridge");
        package.DocumentEditors.Should().ContainSingle();

        var contribution = package.DocumentEditors[0];
        contribution.Should().BeOfType<CustomDocumentEditorContribution>();
        contribution.Id.Should().Be("my-editor-doc");
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".myext");
        ((CustomDocumentEditorContribution)contribution).EntryPoint.Should().Be("index.html");
        contribution.Package.PackageFolder.Should().Be(_tempFolder);
        contribution.Package.HostName.Should().Be("pkg-test-my-editor.celbridge");
    }

    [Test]
    public void LoadPackage_ValidCodeDocument_WithPreview_ReturnsContribution()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var contribution = result.Value.DocumentEditors[0];
        contribution.Should().BeOfType<CodeDocumentEditorContribution>();

        var codeContribution = (CodeDocumentEditorContribution)contribution;
        codeContribution.CodePreview.Should().NotBeNull();
        codeContribution.CodePreview!.EntryPoint.Should().Be("preview/index.html");
        codeContribution.CodeEditor.Should().NotBeNull();
        codeContribution.CodeEditor!.CustomizationScript.Should().Be("customize.js");
    }

    [Test]
    public void LoadPackage_MissingPackageId_ReturnsFailure()
    {
        WritePackageToml("""
            [package]
            name = "No Id"
            version = "1.0.0"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_MissingPackageSection_ReturnsFailure()
    {
        WritePackageToml("""
            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_EmptyFileTypes_ReturnsNoContributions()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        // Document with no document_file_types is skipped (invalid)
        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_InvalidToml_ReturnsFailure()
    {
        var path = Path.Combine(_tempFolder, "package.toml");
        File.WriteAllText(path, "{ not valid toml }");

        var result = PackageManifestLoader.LoadPackage(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_NonExistentFile_ReturnsFailure()
    {
        var path = Path.Combine(_tempFolder, "nonexistent.toml");

        var result = PackageManifestLoader.LoadPackage(path);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_DefaultPriority_IsDefault()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors[0].Priority.Should().Be(EditorPriority.Specialized);
    }

    [Test]
    public void LoadPackage_WithGeneralPriority_UsesGeneral()
    {
        WritePackageToml("""
            [package]
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
            priority = "general"

            [[document_file_types]]
            extension = ".pri"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors[0].Priority.Should().Be(EditorPriority.General);
    }

    [Test]
    public void LoadPackage_WithFeatureFlag_PropagatedToInfo()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.FeatureFlag.Should().Be("my-feature");
        result.Value.DocumentEditors[0].Package.FeatureFlag.Should().Be("my-feature");
    }

    [Test]
    public void LoadPackage_WithoutFeatureFlag_ReturnsNull()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.FeatureFlag.Should().BeNull();
    }


    [Test]
    public void LoadPackage_WithTemplates_ReturnsTemplates()
    {
        WritePackageToml("""
            [package]
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
            template_file = "templates/empty.tmpl"
            default = true

            [[document_templates]]
            id = "example"
            display_name = "Example File"
            template_file = "templates/example.tmpl"
            default = false
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors[0].Templates.Should().HaveCount(2);

        var defaultTemplate = result.Value.DocumentEditors[0].Templates[0];
        defaultTemplate.Id.Should().Be("empty");
        defaultTemplate.DisplayName.Should().Be("Empty File");
        defaultTemplate.TemplateFile.Should().Be("templates/empty.tmpl");
        defaultTemplate.Default.Should().BeTrue();

        var exampleTemplate = result.Value.DocumentEditors[0].Templates[1];
        exampleTemplate.Id.Should().Be("example");
        exampleTemplate.Default.Should().BeFalse();
    }

    [Test]
    public void LoadPackage_WithoutTemplates_ReturnsEmptyList()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors[0].Templates.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_FullDocument_AllFieldsPopulated()
    {
        WritePackageToml("""
            [package]
            id = "test.full-editor"
            name = "Full Editor"
            version = "2.0.0"
            feature_flag = "full-pkg"

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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();

        var package = result.Value;
        package.Info.Name.Should().Be("Full Editor");
        package.Info.FeatureFlag.Should().Be("full-pkg");

        var contribution = package.DocumentEditors[0];
        contribution.Should().BeOfType<CustomDocumentEditorContribution>();
        contribution.Id.Should().Be("full-doc");
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".full");
        ((CustomDocumentEditorContribution)contribution).EntryPoint.Should().Be("index.html");
        contribution.Priority.Should().Be(EditorPriority.Specialized);
        contribution.Templates.Should().HaveCount(1);
    }

    [Test]
    public void LoadPackage_MultipleDocuments_ReturnsAll()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().HaveCount(2);
        result.Value.DocumentEditors[0].Should().BeOfType<CustomDocumentEditorContribution>();
        result.Value.DocumentEditors[0].Id.Should().Be("doc-a");
        result.Value.DocumentEditors[1].Should().BeOfType<CodeDocumentEditorContribution>();
        result.Value.DocumentEditors[1].Id.Should().Be("doc-b");
    }

    [Test]
    public void LoadPackage_CodeEditorOptions_Parsed()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var codeContribution = result.Value.DocumentEditors[0] as CodeDocumentEditorContribution;
        codeContribution.Should().NotBeNull();
        codeContribution!.CodeEditor.Should().NotBeNull();
        codeContribution.CodeEditor!.WordWrap.Should().BeTrue();
        codeContribution.CodeEditor.ScrollBeyondLastLine.Should().BeFalse();
        codeContribution.CodeEditor.MinimapEnabled.Should().BeTrue();
        codeContribution.CodeEditor.CustomizationScript.Should().Be("custom.js");
    }

    [Test]
    public void LoadPackage_MissingDocumentFile_SkipsDocument()
    {
        WritePackageToml("""
            [package]
            id = "test.missing-doc"
            name = "MissingDoc"
            version = "1.0.0"

            [contributes]
            document_editors = ["nonexistent.document.toml"]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_MultipleDocuments_SharePackageInfo()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().HaveCount(2);

        // Both contributions share the same PackageInfo
        result.Value.Info.Name.Should().Be("Shared");
        result.Value.Info.FeatureFlag.Should().Be("shared-flag");
        result.Value.DocumentEditors[0].Package.Name.Should().Be("Shared");
        result.Value.DocumentEditors[1].Package.Name.Should().Be("Shared");
    }

    [Test]
    public void LoadPackage_CustomDocumentWithoutEntryPoint_DefaultsToIndexHtml()
    {
        WritePackageToml("""
            [package]
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

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var customContribution = result.Value.DocumentEditors[0] as CustomDocumentEditorContribution;
        customContribution.Should().NotBeNull();
        customContribution!.EntryPoint.Should().Be("index.html");
    }

    [Test]
    public void LoadPackage_WithModSection_ParsesRequiresToolsAndSecrets()
    {
        WritePackageToml("""
            [package]
            id = "test.mod-section"
            name = "ModSection"
            version = "1.0.0"

            [mod]
            requires_tools = ["app.*", "document.open"]
            requires_secrets = ["spreadjs_license", "spreadjs_designer_license"]

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "mod-section-doc"
            type = "custom"

            [[document_file_types]]
            extension = ".ms"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var info = result.Value.Info;
        info.RequiresTools.Should().Equal("app.*", "document.open");
        info.RequiresSecrets.Should().Equal("spreadjs_license", "spreadjs_designer_license");
    }

    [Test]
    public void LoadPackage_WithoutModSection_DefaultsToEmptyRequirements()
    {
        WritePackageToml("""
            [package]
            id = "test.no-mod"
            name = "NoMod"
            version = "1.0.0"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "no-mod-doc"
            type = "custom"

            [[document_file_types]]
            extension = ".nm"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.RequiresTools.Should().BeEmpty();
        result.Value.Info.RequiresSecrets.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_ModSectionWithNonStringEntries_SkipsInvalid()
    {
        WritePackageToml("""
            [package]
            id = "test.mixed"
            name = "Mixed"
            version = "1.0.0"

            [mod]
            requires_tools = ["app.*", 42, "", "file.read"]

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "mixed-doc"
            type = "custom"

            [[document_file_types]]
            extension = ".mx"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.RequiresTools.Should().Equal("app.*", "file.read");
    }

    private void WritePackageToml(string content)
    {
        File.WriteAllText(Path.Combine(_tempFolder, "package.toml"), content);
    }

    private void WriteDocumentToml(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_tempFolder, fileName), content);
    }
}
