using Celbridge.Packages;
using Celbridge.Tests.Architecture;

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
            name = "test.my-editor"
            title = "My Editor"

            [contributes]
            document_editors = ["editor.document.toml"]
            """);

        WriteDocumentToml("editor.document.toml", """
            [document]
            id = "my-editor-doc"
            type = "custom"
            entry_point = "index.html"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".myext"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();

        var package = result.Value;
        package.Info.Title.Should().Be("My Editor");
        package.Info.PackageFolder.Should().Be(_tempFolder);
        package.DocumentEditors.Should().ContainSingle();

        var contribution = package.DocumentEditors[0];
        contribution.Should().BeOfType<EditorContribution>();
        contribution.Id.Should().Be("my-editor-doc");
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".myext");
        ((EditorContribution)contribution).EntryPoint.Should().Be("index.html");
        contribution.Package.PackageFolder.Should().Be(_tempFolder);
    }

    [Test]
    public void LoadPackage_NameAndTitle_PopulateInfo_StrayAuthorIgnored()
    {
        // 'author' is no longer a manifest field (the publisher comes from
        // Workshop settings), but a legacy manifest that still carries it must
        // load fine with the key simply ignored.
        WritePackageToml("""
            [package]
            name = "my-widget"
            author = "Acme"
            title = "My Widget"

            [contributes]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.Name.Should().Be("my-widget");
        result.Value.Info.Title.Should().Be("My Widget");
    }

    [Test]
    public void LoadPackage_TitleOmitted_DefaultsToEmpty()
    {
        WritePackageToml("""
            [package]
            name = "my-widget"

            [contributes]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.Name.Should().Be("my-widget");
        result.Value.Info.Title.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_MissingPackageName_ReturnsFailure()
    {
        WritePackageToml("""
            [package]
            title = "No Name"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("Celbridge.Notes", Description = "uppercase rejected")]
    [TestCase("CELBRIDGE.notes", Description = "all-caps rejected")]
    [TestCase("has_underscore.notes", Description = "underscore rejected")]
    [TestCase("has spaces.notes", Description = "whitespace rejected")]
    [TestCase(".leading-dot", Description = "leading dot rejected")]
    [TestCase("trailing-dot.", Description = "trailing dot rejected")]
    [TestCase("double..dot", Description = "consecutive dots rejected")]
    [TestCase(".", Description = "bare dot rejected")]
    [TestCase("-leading-hyphen", Description = "leading hyphen rejected")]
    [TestCase("trailing-hyphen-", Description = "trailing hyphen rejected")]
    [TestCase("double--hyphen", Description = "consecutive hyphens rejected")]
    public void LoadPackage_InvalidNameFormat_ReturnsFailure(string invalidName)
    {
        WritePackageToml($"""
            [package]
            name = "{invalidName}"
            title = "Test"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("invalid");
    }

    [Test]
    public void LoadPackage_NameOverMaxLength_ReturnsFailure()
    {
        var overLongName = new string('a', PackageConstants.MaxNameLength + 1);
        WritePackageToml($"""
            [package]
            name = "{overLongName}"
            title = "Test"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("invalid");
    }

    [TestCase("celbridge.notes", Description = "reserved namespace prefix")]
    [TestCase("my-org.my-tool", Description = "dotted namespace")]
    [TestCase("a.b", Description = "minimal dotted")]
    [TestCase("a.b.c.d", Description = "deeply nested")]
    [TestCase("digits123.allowed", Description = "digits in namespace")]
    [TestCase("hyphens-are-fine.here", Description = "hyphens in namespace")]
    [TestCase("flat-name", Description = "flat global namespace name")]
    [TestCase("simple", Description = "single-word flat name")]
    [TestCase("a", Description = "single character")]
    public void LoadPackage_ValidNameFormats_Accepted(string validName)
    {
        WritePackageToml($"""
            [package]
            name = "{validName}"
            title = "Test"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "test-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".test"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
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
            name = "test.empty"
            title = "Empty"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "empty-doc"
            type = "custom"
            display_name = "TestEditor"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        // Document with no document_file_types is skipped (invalid)
        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_FileTypeMissingDisplayName_SkipsDocument()
    {
        WritePackageToml("""
            [package]
            name = "test.no-file-type-display"
            title = "NoFileTypeDisplay"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "nftd-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".nftd"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        // Document with a file type missing display_name is skipped (invalid).
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
            name = "test.basic"
            title = "Basic"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "basic-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".bas"
            display_name = "TestFileType"
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
            name = "test.priority"
            title = "Priority"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "priority-doc"
            type = "custom"
            priority = "general"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".pri"
            display_name = "TestFileType"
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
            name = "test.flagged"
            title = "Flagged"
            feature_flag = "my-feature"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "flagged-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".flag"
            display_name = "TestFileType"
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
            name = "test.noflag"
            title = "NoFlag"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "noflag-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".nf"
            display_name = "TestFileType"
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
            name = "test.templated"
            title = "Templated"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "templated-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".tmpl"
            display_name = "TestFileType"

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
            name = "test.notemplates"
            title = "NoTemplates"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "notemplates-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".nt"
            display_name = "TestFileType"
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
            name = "test.full-editor"
            title = "Full Editor"
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
            display_name = "TestEditor"

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
        package.Info.Title.Should().Be("Full Editor");
        package.Info.FeatureFlag.Should().Be("full-pkg");

        var contribution = package.DocumentEditors[0];
        contribution.Should().BeOfType<EditorContribution>();
        contribution.Id.Should().Be("full-doc");
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".full");
        ((EditorContribution)contribution).EntryPoint.Should().Be("index.html");
        contribution.Priority.Should().Be(EditorPriority.Specialized);
        contribution.Templates.Should().HaveCount(1);
    }

    [Test]
    public void LoadPackage_MultipleDocuments_ReturnsAll()
    {
        WritePackageToml("""
            [package]
            name = "test.multi"
            title = "Multi"

            [contributes]
            document_editors = ["a.document.toml", "b.document.toml"]
            """);

        WriteDocumentToml("a.document.toml", """
            [document]
            id = "doc-a"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".aaa"
            display_name = "TestFileType"
            """);

        WriteDocumentToml("b.document.toml", """
            [document]
            id = "doc-b"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".bbb"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().HaveCount(2);
        result.Value.DocumentEditors[0].Id.Should().Be("doc-a");
        result.Value.DocumentEditors[1].Id.Should().Be("doc-b");
    }

    [Test]
    public void LoadPackage_MissingDocumentFile_SkipsDocument()
    {
        WritePackageToml("""
            [package]
            name = "test.missing-doc"
            title = "MissingDoc"

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
            name = "test.shared"
            title = "Shared"
            feature_flag = "shared-flag"

            [contributes]
            document_editors = ["a.document.toml", "b.document.toml"]
            """);

        WriteDocumentToml("a.document.toml", """
            [document]
            id = "doc-a"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".aaa"
            display_name = "TestFileType"
            """);

        WriteDocumentToml("b.document.toml", """
            [document]
            id = "doc-b"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".bbb"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().HaveCount(2);

        // Both contributions share the same PackageInfo
        result.Value.Info.Title.Should().Be("Shared");
        result.Value.Info.FeatureFlag.Should().Be("shared-flag");
        result.Value.DocumentEditors[0].Package.Title.Should().Be("Shared");
        result.Value.DocumentEditors[1].Package.Title.Should().Be("Shared");
    }

    [Test]
    public void LoadPackage_CustomDocumentWithoutEntryPoint_DefaultsToIndexHtml()
    {
        WritePackageToml("""
            [package]
            name = "test.no-entry"
            title = "NoEntry"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "no-entry-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".ne"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var customContribution = result.Value.DocumentEditors[0] as EditorContribution;
        customContribution.Should().NotBeNull();
        customContribution!.EntryPoint.Should().Be("index.html");
    }

    [Test]
    public void LoadPackage_WithPermissionsSection_ParsesPermittedTools()
    {
        WritePackageToml("""
            [package]
            name = "test.permissions-section"
            title = "PermissionsSection"

            [permissions]
            tools = ["app.*", "document.open"]

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "permissions-section-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".ms"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var info = result.Value.Info;
        info.PermittedTools.Should().Equal("app.*", "document.open");
    }

    [Test]
    public void LoadPackage_SecretsParameter_PopulatesPackageInfo()
    {
        WritePackageToml("""
            [package]
            name = "test.secrets"
            title = "WithSecrets"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "secrets-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".sec"
            display_name = "TestFileType"
            """);

        var suppliedSecrets = new Dictionary<string, string>
        {
            ["license"] = "abc123",
            ["designer_license"] = "def456",
        };

        var result = PackageManifestLoader.LoadPackage(
            Path.Combine(_tempFolder, "package.toml"),
            secrets: suppliedSecrets);

        result.IsSuccess.Should().BeTrue();
        var info = result.Value.Info;
        info.Secrets.Should().HaveCount(2);
        info.Secrets["license"].Should().Be("abc123");
        info.Secrets["designer_license"].Should().Be("def456");
    }

    [Test]
    public void LoadPackage_NoSecretsParameter_LeavesSecretsEmpty()
    {
        WritePackageToml("""
            [package]
            name = "test.no-secrets"
            title = "NoSecrets"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "no-secrets-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".ns"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.Secrets.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_WithoutPermissionsSection_DefaultsToEmpty()
    {
        WritePackageToml("""
            [package]
            name = "test.no-permissions"
            title = "NoPermissions"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "no-permissions-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".nm"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.PermittedTools.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_PermissionsSectionWithNonStringEntries_SkipsInvalid()
    {
        WritePackageToml("""
            [package]
            name = "test.mixed"
            title = "Mixed"

            [permissions]
            tools = ["app.*", 42, "", "file.read"]

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "mixed-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".mx"
            display_name = "TestFileType"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.PermittedTools.Should().Equal("app.*", "file.read");
    }

    [Test]
    public void LoadPackage_ExtensionsFile_ExpandsToEachJsonKey()
    {
        WritePackageToml("""
            [package]
            name = "test.code"
            title = "Code"

            [contributes]
            document_editors = ["code.document.toml"]
            """);

        WriteDocumentToml("code.document.toml", """
            [document]
            id = "code-doc"
            type = "custom"
            entry_point = "index.html"
            display_name = "TestEditor"

            [[document_file_types]]
            extensions_file = "types.json"
            display_name = "Code_FileType_Code"
            """);

        File.WriteAllText(Path.Combine(_tempFolder, "types.json"),
            """{ ".js": "javascript", ".py": "python", ".cs": "csharp" }""");

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var contribution = result.Value.DocumentEditors[0];
        contribution.FileTypes.Select(fileType => fileType.FileExtension)
            .Should().BeEquivalentTo([".js", ".py", ".cs"]);
        contribution.FileTypes.Should().AllSatisfy(fileType =>
            fileType.DisplayName.Should().Be("Code_FileType_Code"));
    }

    [Test]
    public void LoadPackage_ExtensionsFile_MissingFile_DocumentSkipped()
    {
        WritePackageToml("""
            [package]
            name = "test.missing-ext"
            title = "Missing"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "missing-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extensions_file = "nonexistent.json"
            display_name = "X"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        // A broken document manifest is silently skipped — matches the existing
        // behavior for missing file-types sections and other per-document errors.
        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_ExtensionsFile_CombinedWithExtension_DocumentSkipped()
    {
        WritePackageToml("""
            [package]
            name = "test.conflict"
            title = "Conflict"

            [contributes]
            document_editors = ["doc.document.toml"]
            """);

        WriteDocumentToml("doc.document.toml", """
            [document]
            id = "conflict-doc"
            type = "custom"
            display_name = "TestEditor"

            [[document_file_types]]
            extension = ".x"
            extensions_file = "types.json"
            display_name = "X"
            """);

        File.WriteAllText(Path.Combine(_tempFolder, "types.json"), """{ ".y": "yaml" }""");

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_UtilityDocument_ParsesDescriptorAndDerivesExtension()
    {
        WritePackageToml("""
            [package]
            name = "test.emoji"
            title = "Emoji Renderer"

            [contributes]
            document_editors = ["emoji.document.toml"]
            """);

        WriteDocumentToml("emoji.document.toml", """
            [document]
            id = "emoji-renderer"
            type = "custom"
            entry_point = "index.html"

            [utility]
            resource = "utils:settings._emoji"
            template = "templates/default._emoji"
            icon = "emoji-smile"
            tooltip = "Emoji_Utility_Tooltip"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().ContainSingle();

        var contribution = (EditorContribution)result.Value.DocumentEditors[0];
        contribution.IsUtility.Should().BeTrue();

        // The editor extension is derived from the backing resource, and the display name defaults to the
        // tooltip key (a utility has no separate label field).
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be("._emoji");
        contribution.DisplayName.Should().Be("Emoji_Utility_Tooltip");

        var descriptor = contribution.UtilityDescriptor!;
        descriptor.Resource.Should().Be("utils:settings._emoji");
        descriptor.Template.Should().Be("templates/default._emoji");
        descriptor.Icon.Should().Be("emoji-smile");
        descriptor.Tooltip.Should().Be("Emoji_Utility_Tooltip");
    }

    [Test]
    public void LoadPackage_UtilityDefaults_TemplateEmpty()
    {
        WritePackageToml("""
            [package]
            name = "test.emoji"
            title = "Emoji Renderer"

            [contributes]
            document_editors = ["emoji.document.toml"]
            """);

        WriteDocumentToml("emoji.document.toml", """
            [document]
            id = "emoji-renderer"
            type = "custom"

            [utility]
            resource = "utils:settings._emoji"
            icon = "emoji-smile"
            tooltip = "Emoji_Utility_Tooltip"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        var contribution = (EditorContribution)result.Value.DocumentEditors[0];
        contribution.UtilityDescriptor!.Template.Should().BeEmpty();
    }

    // Loads the real bundled utility manifests (not synthetic ones) so a fixture that regresses -- for
    // example by declaring both [utility] and [[document_file_types]], which the loader rejects, silently
    // dropping the editor -- fails the build instead of only surfacing in a manual in-app run.
    [TestCase("Notepad")]
    [TestCase("Process")]
    public void LoadPackage_BundledUtilityFixture_RegistersUtilityContribution(string editorFolder)
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty("the test must locate the repository Source folder to read bundled fixtures");

        var packagePath = Path.Combine(sourceFolder, "Modules", "Celbridge.DocumentEditors", "Editors", editorFolder, "package.toml");
        File.Exists(packagePath).Should().BeTrue($"the bundled utility manifest should exist at '{packagePath}'");

        var result = PackageManifestLoader.LoadPackage(packagePath);

        result.IsSuccess.Should().BeTrue();

        var contribution = result.Value.DocumentEditors.Should().ContainSingle()
            .Which.Should().BeOfType<EditorContribution>().Which;
        contribution.IsUtility.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_UtilityWithFileTypes_IsRejected()
    {
        WritePackageToml("""
            [package]
            name = "test.emoji"
            title = "Emoji Renderer"

            [contributes]
            document_editors = ["emoji.document.toml"]
            """);

        // A utility cannot also claim a file type across the project; the two forms are mutually exclusive.
        WriteDocumentToml("emoji.document.toml", """
            [document]
            id = "emoji-renderer"
            type = "custom"

            [utility]
            resource = "utils:settings._emoji"
            icon = "emoji-smile"
            tooltip = "Emoji_Utility_Tooltip"

            [[document_file_types]]
            extension = ".emoji"
            display_name = "Emoji"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        // A failed document manifest is skipped, so the package loads with no document editors.
        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_UtilityMissingResource_IsRejected()
    {
        WritePackageToml("""
            [package]
            name = "test.emoji"
            title = "Emoji Renderer"

            [contributes]
            document_editors = ["emoji.document.toml"]
            """);

        WriteDocumentToml("emoji.document.toml", """
            [document]
            id = "emoji-renderer"
            type = "custom"

            [utility]
            icon = "emoji-smile"
            tooltip = "Emoji_Utility_Tooltip"
            """);

        var result = PackageManifestLoader.LoadPackage(Path.Combine(_tempFolder, "package.toml"));

        result.IsSuccess.Should().BeTrue();
        result.Value.DocumentEditors.Should().BeEmpty();
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
