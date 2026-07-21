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
    public void LoadPackage_ValidDocumentEditor_ReturnsContribution()
    {
        WritePackageToml("""
            [package]
            name = "test.my-editor"
            title = "My Editor"

            [contributes]
            editors = ["editor.editor.toml"]
            """);

        WriteEditorToml("editor.editor.toml", """
            [editor]
            id = "my-editor"
            type = "document"
            entry-point = "index.html"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".myext"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();

        var package = result.Value;
        package.Info.Title.Should().Be("My Editor");
        package.Info.PackageFolder.Should().Be(_tempFolder);
        package.Editors.Should().ContainSingle();

        var contribution = package.Editors[0];
        contribution.Id.Should().Be("my-editor");
        contribution.DisplayName.Should().Be("TestEditor");
        contribution.FileTypes.Should().ContainSingle().Which.FileExtension.Should().Be(".myext");
        contribution.EntryPoint.Should().Be("index.html");
        contribution.Package.PackageFolder.Should().Be(_tempFolder);
        contribution.ManifestPath.Should().Be(Path.Combine(_tempFolder, "editor.editor.toml"));
    }

    [Test]
    public void LoadPackage_FileTypeExtensionWithoutLeadingDot_ReturnsFailure()
    {
        // An extension with no leading dot would register an editor that never matches any file,
        // so it is rejected at load rather than failing silently.
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-editor"
            type = "document"
            entry-point = "index.html"
            display-name = "Widget"

            [[file-types]]
            extension = "myext"
            display-name = "WidgetFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("well-formed file extension");
    }

    [Test]
    public void LoadPackage_FileTypeExtensionUppercase_IsLowercased()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-editor"
            type = "document"
            entry-point = "index.html"
            display-name = "Widget"

            [[file-types]]
            extension = ".MyExt"
            display-name = "WidgetFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].FileTypes.Should().ContainSingle()
            .Which.FileExtension.Should().Be(".myext");
    }

    [Test]
    public void LoadPackage_NameAndTitle_PopulateInfo_StrayAuthorIgnored()
    {
        // 'author' is not a manifest field, but a manifest that carries it must still
        // load, with the key ignored.
        WritePackageToml("""
            [package]
            name = "my-widget"
            author = "Acme"
            title = "My Widget"

            [contributes]
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.Name.Should().Be("my-widget");
        result.Value.Info.Title.Should().Be("My Widget");
    }

    [Test]
    public void LoadPackage_RetiredFeatureFlagKey_Ignored()
    {
        // 'feature_flag' was removed from the schema. A manifest that still carries it
        // loads normally with the key ignored.
        WritePackageToml("""
            [package]
            name = "my-widget"
            title = "My Widget"
            feature_flag = "my-feature"

            [contributes]
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.Name.Should().Be("my-widget");
    }

    [Test]
    public void LoadPackage_TitleOmitted_DefaultsToEmpty()
    {
        WritePackageToml("""
            [package]
            name = "my-widget"

            [contributes]
            """);

        var result = LoadPackage();

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
            """);

        var result = LoadPackage();

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
            """);

        var result = LoadPackage();

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
            """);

        var result = LoadPackage();

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
            editors = ["editor.editor.toml"]
            """);

        WriteEditorToml("editor.editor.toml", """
            [editor]
            id = "test-editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".test"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_MissingPackageSection_ReturnsFailure()
    {
        WritePackageToml("""
            [contributes]
            editors = ["editor.editor.toml"]
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
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
    public void LoadPackage_EditorReferenceWithWrongExtension_ReturnsFailure()
    {
        // References in [contributes].editors must use the ".editor.toml" extension.
        WritePackageToml("""
            [package]
            name = "test.wrong-ext"
            title = "WrongExtension"

            [contributes]
            editors = ["editor.document.toml"]
            """);

        WriteEditorToml("editor.document.toml", """
            [editor]
            id = "wrong-ext"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".we"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(".editor.toml");
    }

    [Test]
    public void LoadPackage_MissingEditorManifest_ReturnsFailure()
    {
        WritePackageToml("""
            [package]
            name = "test.missing-editor"
            title = "MissingEditor"

            [contributes]
            editors = ["nonexistent.editor.toml"]
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_DuplicateContributionIds_ReturnsFailure()
    {
        WritePackageToml("""
            [package]
            name = "test.duplicate"
            title = "Duplicate"

            [contributes]
            editors = ["a.editor.toml", "b.editor.toml"]
            """);

        WriteEditorToml("a.editor.toml", """
            [editor]
            id = "same-id"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".aaa"
            display-name = "TestFileType"
            """);

        WriteEditorToml("b.editor.toml", """
            [editor]
            id = "same-id"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".bbb"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("same-id");
    }

    [Test]
    public void LoadPackage_InvalidEditorManifest_FailsWholePackage()
    {
        // One invalid editor manifest fails the whole package rather than being
        // silently skipped.
        WritePackageToml("""
            [package]
            name = "test.partial"
            title = "Partial"

            [contributes]
            editors = ["good.editor.toml", "bad.editor.toml"]
            """);

        WriteEditorToml("good.editor.toml", """
            [editor]
            id = "good"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".good"
            display-name = "TestFileType"
            """);

        WriteEditorToml("bad.editor.toml", """
            [editor]
            id = "bad"
            type = "document"

            [[file-types]]
            extension = ".bad"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_EditorMissingEditorSection_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [[file-types]]
            extension = ".ne"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_EditorMissingId_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".ni"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("My-Editor", Description = "uppercase rejected")]
    [TestCase("has_underscore", Description = "underscore rejected")]
    [TestCase("has.dot", Description = "dot rejected")]
    [TestCase("has space", Description = "whitespace rejected")]
    public void LoadPackage_EditorInvalidIdCharset_ReturnsFailure(string invalidId)
    {
        WriteSingleEditorPackage($"""
            [editor]
            id = "{invalidId}"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".iid"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_EditorMissingType_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "no-type"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".nt"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("type");
    }

    [TestCase("custom", Description = "retired type value rejected")]
    [TestCase("code", Description = "retired type value rejected")]
    [TestCase("widget", Description = "unknown type value rejected")]
    public void LoadPackage_EditorUnknownType_ReturnsFailure(string unknownType)
    {
        WriteSingleEditorPackage($"""
            [editor]
            id = "unknown-type"
            type = "{unknownType}"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".ut"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(unknownType);
    }

    [Test]
    public void LoadPackage_UtilityWithoutUtilitySection_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "bare-utility"
            type = "utility"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_UtilityWithFileTypes_ReturnsFailure()
    {
        // A utility owns its own state file and must not claim file extensions.
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-renderer"
            type = "utility"

            [utility]
            resource-extension = "._widget"
            icon = "star"
            tooltip = "Widget_Utility_Tooltip"

            [[file-types]]
            extension = ".widget"
            display-name = "Widget"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_DocumentWithUtilitySection_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "conflicted"
            type = "document"
            display-name = "TestEditor"

            [utility]
            resource-extension = "._conflicted"
            icon = "sticky"
            tooltip = "Conflicted_Tooltip"

            [[file-types]]
            extension = ".con"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_DocumentWithoutFileTypes_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "no-file-types"
            type = "document"
            display-name = "TestEditor"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_FileTypeMissingDisplayName_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "nftd-editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".nftd"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_WithTemplates_ReturnsTemplates()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "templated"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".tmpl"
            display-name = "TestFileType"

            [[templates]]
            id = "empty"
            display-name = "Empty File"
            template-file = "templates/empty.tmpl"
            default = true

            [[templates]]
            id = "example"
            display-name = "Example File"
            template-file = "templates/example.tmpl"
            default = false
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].Templates.Should().HaveCount(2);

        var defaultTemplate = result.Value.Editors[0].Templates[0];
        defaultTemplate.Id.Should().Be("empty");
        defaultTemplate.DisplayName.Should().Be("Empty File");
        defaultTemplate.TemplateFile.Should().Be("templates/empty.tmpl");
        defaultTemplate.Default.Should().BeTrue();

        var exampleTemplate = result.Value.Editors[0].Templates[1];
        exampleTemplate.Id.Should().Be("example");
        exampleTemplate.Default.Should().BeFalse();
    }

    [Test]
    public void LoadPackage_WithoutTemplates_ReturnsEmptyList()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "no-templates"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".nt"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].Templates.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_TemplatesWithExternalContent_ReturnsFailure()
    {
        // An external-content editor never writes a starter file, so templates are invalid.
        WriteSingleEditorPackage("""
            [editor]
            id = "external"
            type = "document"
            external-content = true
            display-name = "TestEditor"

            [[file-types]]
            extension = ".ext"
            display-name = "TestFileType"

            [[templates]]
            id = "empty"
            display-name = "Empty"
            template-file = "templates/empty.ext"
            default = true
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_WithoutEntryPoint_DefaultsToIndexHtml()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "no-entry"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".ne"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].EntryPoint.Should().Be("index.html");
    }

    [Test]
    public void LoadPackage_MultipleEditors_ReturnsAll()
    {
        WritePackageToml("""
            [package]
            name = "test.multi"
            title = "Multi"

            [contributes]
            editors = ["a.editor.toml", "b.editor.toml"]
            """);

        WriteEditorToml("a.editor.toml", """
            [editor]
            id = "editor-a"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".aaa"
            display-name = "TestFileType"
            """);

        WriteEditorToml("b.editor.toml", """
            [editor]
            id = "editor-b"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".bbb"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors.Should().HaveCount(2);
        result.Value.Editors[0].Id.Should().Be("editor-a");
        result.Value.Editors[1].Id.Should().Be("editor-b");
    }

    [Test]
    public void LoadPackage_MultipleEditors_SharePackageInfo()
    {
        WritePackageToml("""
            [package]
            name = "test.shared"
            title = "Shared"

            [contributes]
            editors = ["a.editor.toml", "b.editor.toml"]
            """);

        WriteEditorToml("a.editor.toml", """
            [editor]
            id = "editor-a"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".aaa"
            display-name = "TestFileType"
            """);

        WriteEditorToml("b.editor.toml", """
            [editor]
            id = "editor-b"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".bbb"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors.Should().HaveCount(2);

        result.Value.Info.Title.Should().Be("Shared");
        result.Value.Editors[0].Package.Title.Should().Be("Shared");
        result.Value.Editors[1].Package.Title.Should().Be("Shared");
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
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        var info = result.Value.Info;
        info.PermittedTools.Should().Equal("app.*", "document.open");
    }

    [Test]
    public void LoadPackage_WithoutPermissionsSection_DefaultsToEmpty()
    {
        WritePackageToml("""
            [package]
            name = "test.no-permissions"
            title = "NoPermissions"

            [contributes]
            """);

        var result = LoadPackage();

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
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.PermittedTools.Should().Equal("app.*", "file.read");
    }

    [Test]
    public void LoadPackage_SecretsParameter_PopulatesPackageInfo()
    {
        WritePackageToml("""
            [package]
            name = "test.secrets"
            title = "WithSecrets"

            [contributes]
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
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Info.Secrets.Should().BeEmpty();
    }

    [Test]
    public void LoadPackage_FromCatalogLanguages_ClaimsEveryCatalogExtension()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "code-editor"
            type = "document"
            entry-point = "index.html"
            display-name = "TestEditor"

            [[file-types]]
            from-catalog = "languages"
            display-name = "Code_FileType_Code"
            """);

        var result = LoadPackage(CatalogWithLanguages(".js", ".py", ".cs"));

        result.IsSuccess.Should().BeTrue();
        var contribution = result.Value.Editors[0];
        contribution.FileTypes.Select(fileType => fileType.FileExtension)
            .Should().BeEquivalentTo([".js", ".py", ".cs"]);
        contribution.FileTypes.Should().AllSatisfy(fileType =>
            fileType.DisplayName.Should().Be("Code_FileType_Code"));
    }

    [Test]
    public void LoadPackage_FromCatalogWithEmptyCatalog_ReturnsFailure()
    {
        // The catalog is the editor's only source of extensions, so an unloaded or empty catalog
        // leaves a document editor claiming nothing, which is a load failure.
        WriteSingleEditorPackage("""
            [editor]
            id = "code-editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            from-catalog = "languages"
            display-name = "Code_FileType_Code"
            """);

        var result = LoadPackage(CatalogWithLanguages());

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("at least one file type");
    }

    [Test]
    public void LoadPackage_FromCatalogUnknownValue_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "code-editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            from-catalog = "images"
            display-name = "X"
            """);

        var result = LoadPackage(CatalogWithLanguages(".js"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("from-catalog");
    }

    [Test]
    public void LoadPackage_FromCatalogCombinedWithExtension_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "conflict"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".x"
            from-catalog = "languages"
            display-name = "X"
            """);

        var result = LoadPackage(CatalogWithLanguages(".y"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_UtilityEditor_ParsesDescriptor()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-renderer"
            type = "utility"
            entry-point = "index.html"
            display-name = "Widget_Utility_DisplayName"
            description = "Widget_Utility_Description"

            [utility]
            resource-extension = "._widget"
            template = "templates/default._widget"
            icon = "star"
            lazy-load = true
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors.Should().ContainSingle();

        var contribution = result.Value.Editors[0];
        contribution.IsUtility.Should().BeTrue();

        // A utility claims no file extensions. Its display name and description are its own manifest keys.
        contribution.FileTypes.Should().BeEmpty();
        contribution.DisplayName.Should().Be("Widget_Utility_DisplayName");
        contribution.Description.Should().Be("Widget_Utility_Description");

        var descriptor = contribution.UtilityDescriptor!;
        descriptor.ResourceExtension.Should().Be("._widget");
        descriptor.Template.Should().Be("templates/default._widget");
        descriptor.Icon.Should().Be("star");
        descriptor.LazyLoad.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_UtilityDefaults_TemplateEmptyAndLazyLoadFalse()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-renderer"
            type = "utility"
            display-name = "Widget_Utility_DisplayName"

            [utility]
            resource-extension = "._widget"
            icon = "star"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        var descriptor = result.Value.Editors[0].UtilityDescriptor!;
        descriptor.Template.Should().BeEmpty();
        descriptor.LazyLoad.Should().BeFalse();
    }

    [Test]
    public void LoadPackage_UtilityResourceExtension_StoredLowercase()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-renderer"
            type = "utility"
            display-name = "Widget_Utility_DisplayName"

            [utility]
            resource-extension = "._Widget"
            icon = "star"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].UtilityDescriptor!.ResourceExtension.Should().Be("._widget");
    }

    [Test]
    public void LoadPackage_UtilityMissingResourceExtension_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-renderer"
            type = "utility"
            display-name = "Widget_Utility_DisplayName"

            [utility]
            icon = "star"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("_widget", Description = "missing leading dot")]
    [TestCase(".", Description = "bare dot")]
    public void LoadPackage_UtilityInvalidResourceExtension_ReturnsFailure(string invalidExtension)
    {
        WriteSingleEditorPackage($"""
            [editor]
            id = "widget-renderer"
            type = "utility"
            display-name = "Widget_Utility_DisplayName"

            [utility]
            resource-extension = "{invalidExtension}"
            icon = "star"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_UtilityMissingIcon_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-renderer"
            type = "utility"
            display-name = "Widget_Utility_DisplayName"

            [utility]
            resource-extension = "._widget"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_UtilityMissingDisplayName_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "widget-renderer"
            type = "utility"

            [utility]
            resource-extension = "._widget"
            icon = "star"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    // Loads the real bundled utility manifest from the repo rather than a synthetic fixture.
    [TestCase("UtilityDemo", "._utildemo")]
    public void LoadPackage_BundledUtilityFixture_RegistersUtilityContribution(string editorFolder, string expectedResourceExtension)
    {
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        sourceFolder.Should().NotBeEmpty("the test must locate the repository Source folder to read bundled fixtures");

        var packagePath = Path.Combine(sourceFolder, "Modules", "Celbridge.DocumentEditors", "Editors", editorFolder, "package.toml");
        File.Exists(packagePath).Should().BeTrue($"the bundled utility manifest should exist at '{packagePath}'");

        var result = PackageManifestLoader.LoadPackage(packagePath);

        result.IsSuccess.Should().BeTrue();

        var contribution = result.Value.Editors.Should().ContainSingle().Which;
        contribution.IsUtility.Should().BeTrue();
        contribution.FileTypes.Should().BeEmpty();
        contribution.UtilityDescriptor!.ResourceExtension.Should().Be(expectedResourceExtension);
    }

    [Test]
    public void LoadPackage_ConfigDescriptors_ParseAllTypesAndEncodeDefaults()
    {
        WriteEditorWithConfig("""
            [[config]]
            key = "auto-save"
            type = "bool"
            default = true
            display-name = "Config_AutoSave"
            description = "Config_AutoSave_Description"

            [[config]]
            key = "greeting"
            type = "string"
            default = "hello"
            display-name = "Config_Greeting"

            [[config]]
            key = "font-size"
            type = "number"
            default = 14
            display-name = "Config_FontSize"

            [[config]]
            key = "scale"
            type = "number"
            default = 1.5
            display-name = "Config_Scale"

            [[config]]
            key = "shell"
            type = "enum"
            values = ["python", "pwsh"]
            default = "python"
            display-name = "Config_Shell"

            [[config]]
            key = "startup-files"
            type = "string-list"
            default = ["a.py", "b.py"]
            display-name = "Config_StartupFiles"

            [[config]]
            key = "verbose"
            type = "bool"
            display-name = "Config_Verbose"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        var descriptors = result.Value.Editors[0].ConfigDescriptors;
        descriptors.Should().HaveCount(7);

        var autoSave = descriptors[0];
        autoSave.Key.Should().Be("auto-save");
        autoSave.Type.Should().Be(ConfigValueType.Bool);
        autoSave.DefaultValue.Should().Be("true");
        autoSave.DisplayName.Should().Be("Config_AutoSave");
        autoSave.Description.Should().Be("Config_AutoSave_Description");

        var greeting = descriptors[1];
        greeting.Type.Should().Be(ConfigValueType.String);
        greeting.DefaultValue.Should().Be("hello");
        greeting.Description.Should().BeEmpty();

        var fontSize = descriptors[2];
        fontSize.Type.Should().Be(ConfigValueType.Number);
        fontSize.DefaultValue.Should().Be("14");

        var scale = descriptors[3];
        scale.DefaultValue.Should().Be("1.5");

        var shell = descriptors[4];
        shell.Type.Should().Be(ConfigValueType.Enum);
        shell.Values.Should().Equal("python", "pwsh");
        shell.DefaultValue.Should().Be("python");

        var startupFiles = descriptors[5];
        startupFiles.Type.Should().Be(ConfigValueType.StringList);
        startupFiles.DefaultValue.Should().Be("""["a.py","b.py"]""");

        var verbose = descriptors[6];
        verbose.DefaultValue.Should().BeNull();
    }

    [Test]
    public void LoadPackage_ConfigDescriptorMissingKey_ReturnsFailure()
    {
        WriteEditorWithConfig("""
            [[config]]
            type = "string"
            display-name = "Config_NoKey"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("Shell", Description = "uppercase rejected")]
    [TestCase("my_key", Description = "underscore rejected")]
    [TestCase("has.dot", Description = "dot rejected")]
    public void LoadPackage_ConfigDescriptorInvalidKeyCharset_ReturnsFailure(string invalidKey)
    {
        WriteEditorWithConfig($"""
            [[config]]
            key = "{invalidKey}"
            type = "string"
            display-name = "Config_BadKey"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("package")]
    [TestCase("contribution")]
    [TestCase("disabled")]
    [TestCase("enabled")]
    public void LoadPackage_ConfigDescriptorReservedKey_ReturnsFailure(string reservedKey)
    {
        WriteEditorWithConfig($"""
            [[config]]
            key = "{reservedKey}"
            type = "string"
            display-name = "Config_Reserved"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(reservedKey);
    }

    [Test]
    public void LoadPackage_ConfigDescriptorDuplicateKey_ReturnsFailure()
    {
        WriteEditorWithConfig("""
            [[config]]
            key = "shell"
            type = "string"
            display-name = "Config_Shell"

            [[config]]
            key = "shell"
            type = "bool"
            display-name = "Config_Shell_Again"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_ConfigDescriptorUnknownType_ReturnsFailure()
    {
        WriteEditorWithConfig("""
            [[config]]
            key = "mystery"
            type = "date"
            display-name = "Config_Mystery"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_ConfigDescriptorEnumWithoutValues_ReturnsFailure()
    {
        WriteEditorWithConfig("""
            [[config]]
            key = "shell"
            type = "enum"
            display-name = "Config_Shell"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_ConfigDescriptorValuesOnNonEnumType_ReturnsFailure()
    {
        WriteEditorWithConfig("""
            [[config]]
            key = "greeting"
            type = "string"
            values = ["hello", "goodbye"]
            display-name = "Config_Greeting"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void LoadPackage_ConfigDescriptorMissingDisplayName_ReturnsFailure()
    {
        WriteEditorWithConfig("""
            [[config]]
            key = "greeting"
            type = "string"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("bool", "\"yes\"", Description = "string default on a bool descriptor")]
    [TestCase("number", "\"12\"", Description = "string default on a number descriptor")]
    [TestCase("enum", "\"ruby\"", Description = "enum default outside the declared values")]
    [TestCase("string-list", "\"a.py\"", Description = "scalar default on a string-list descriptor")]
    public void LoadPackage_ConfigDescriptorInvalidDefault_ReturnsFailure(string typeValue, string defaultLiteral)
    {
        string valuesLine;
        if (typeValue == "enum")
        {
            valuesLine = """values = ["python", "pwsh"]""";
        }
        else
        {
            valuesLine = string.Empty;
        }

        WriteEditorWithConfig($"""
            [[config]]
            key = "checked"
            type = "{typeValue}"
            {valuesLine}
            default = {defaultLiteral}
            display-name = "Config_Checked"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("required", ActivationPolicy.Required)]
    [TestCase("recommended", ActivationPolicy.Recommended)]
    [TestCase("optional", ActivationPolicy.Optional)]
    public void LoadPackage_EditorActivation_ParsesPolicy(string activationValue, ActivationPolicy expected)
    {
        WriteSingleEditorPackage($"""
            [editor]
            id = "activated"
            type = "document"
            display-name = "TestEditor"
            activation = "{activationValue}"

            [[file-types]]
            extension = ".act"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].Activation.Should().Be(expected);
    }

    [Test]
    public void LoadPackage_EditorWithoutActivation_DefaultsToRequired()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "unmarked"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".unm"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].Activation.Should().Be(ActivationPolicy.Required);
    }

    [Test]
    public void LoadPackage_EditorInvalidActivation_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "bad-activation"
            type = "document"
            display-name = "TestEditor"
            activation = "sometimes"

            [[file-types]]
            extension = ".ba"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("activation");
    }

    [TestCase("text", FileTypeCategory.Text)]
    [TestCase("image", FileTypeCategory.Image)]
    [TestCase("data", FileTypeCategory.Data)]
    [TestCase("document", FileTypeCategory.Document)]
    public void LoadPackage_FileTypeCategory_ParsesCategory(string categoryValue, FileTypeCategory expected)
    {
        WriteSingleEditorPackage($"""
            [editor]
            id = "categorized"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".cat"
            display-name = "TestFileType"
            category = "{categoryValue}"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].FileTypes[0].Category.Should().Be(expected);
    }

    [Test]
    public void LoadPackage_FileTypeWithoutCategory_HasNullCategory()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "uncategorized"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".unc"
            display-name = "TestFileType"
            """);

        var result = LoadPackage();

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].FileTypes[0].Category.Should().BeNull();
    }

    [Test]
    public void LoadPackage_FileTypeInvalidCategory_ReturnsFailure()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "bad-category"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".bc"
            display-name = "TestFileType"
            category = "spreadsheet"
            """);

        var result = LoadPackage();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("category");
    }

    [Test]
    public void LoadPackage_FromCatalogCategory_AppliesToEveryClaimedType()
    {
        WriteSingleEditorPackage("""
            [editor]
            id = "code-editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            from-catalog = "languages"
            display-name = "Code_FileType_Code"
            category = "text"
            """);

        var result = LoadPackage(CatalogWithLanguages(".js", ".py"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Editors[0].FileTypes.Should().AllSatisfy(fileType =>
            fileType.Category.Should().Be(FileTypeCategory.Text));
    }

    private static IFileTypeCatalog CatalogWithLanguages(params string[] extensions)
    {
        var catalog = Substitute.For<IFileTypeCatalog>();
        catalog.LanguageExtensions.Returns(extensions);
        return catalog;
    }

    private Result<Package> LoadPackage(IFileTypeCatalog? fileTypeCatalog = null)
    {
        return PackageManifestLoader.LoadPackage(
            Path.Combine(_tempFolder, "package.toml"),
            fileTypeCatalog: fileTypeCatalog);
    }

    private void WritePackageToml(string content)
    {
        File.WriteAllText(Path.Combine(_tempFolder, "package.toml"), content);
    }

    private void WriteEditorToml(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_tempFolder, fileName), content);
    }

    // Writes a package.toml referencing a single editor manifest with the given content.
    private void WriteSingleEditorPackage(string editorManifestContent)
    {
        WritePackageToml("""
            [package]
            name = "test.widget"
            title = "Widget"

            [contributes]
            editors = ["editor.editor.toml"]
            """);

        WriteEditorToml("editor.editor.toml", editorManifestContent);
    }

    // Writes a valid document editor manifest with the given [[config]] entries appended.
    private void WriteEditorWithConfig(string configToml)
    {
        WriteSingleEditorPackage($"""
            [editor]
            id = "widget-editor"
            type = "document"
            display-name = "TestEditor"

            [[file-types]]
            extension = ".widget"
            display-name = "TestFileType"

            {configToml}
            """);
    }
}
