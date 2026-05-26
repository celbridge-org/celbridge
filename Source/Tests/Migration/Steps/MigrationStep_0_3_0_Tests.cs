using Celbridge.Projects;
using Celbridge.Projects.MigrationSteps;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Tests.Migration.Steps;

/// <summary>
/// Unit tests for MigrationStep_0_3_0 which renames package.toml to package.cel,
/// *.document.toml to *.document.cel, and *.webview to *.webview.cel (converting
/// the JSON body to TOML at the same time).
/// </summary>
[TestFixture]
public class MigrationStep_0_3_0_Tests
{
    private ILogger<MigrationContext> _mockLogger = null!;
    private MigrationStep_0_3_0 _step = null!;
    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private string _projectDataFolderPath = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<MigrationContext>();
        _step = new MigrationStep_0_3_0();

        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"MigrationStep_0_3_0_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectFolderPath);

        _projectFilePath = Path.Combine(_projectFolderPath, "test.celbridge");
        _projectDataFolderPath = Path.Combine(_projectFolderPath, ProjectConstants.MetaDataFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors so they do not mask test failures.
            }
        }
    }

    [Test]
    public void TargetVersion_IsZeroDotThreeDotZero()
    {
        _step.TargetVersion.Should().Be(new Version("0.3.0"));
    }

    [Test]
    public async Task ApplyAsync_RenamesPackageTomlToPackageCel()
    {
        // Arrange
        WriteMinimalProjectFile();
        var packageDir = Path.Combine(_projectFolderPath, "packages", "my-package");
        Directory.CreateDirectory(packageDir);
        var oldManifestPath = Path.Combine(packageDir, "package.toml");
        await File.WriteAllTextAsync(oldManifestPath, "[package]\nid = \"my-package\"\nname = \"My Package\"\nversion = \"1.0.0\"\n");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(oldManifestPath).Should().BeFalse();
        File.Exists(Path.Combine(packageDir, "package.cel")).Should().BeTrue();
    }

    [Test]
    public async Task ApplyAsync_RenamesDocumentTomlToDocumentCel()
    {
        // Arrange
        WriteMinimalProjectFile();
        var packageDir = Path.Combine(_projectFolderPath, "packages", "my-package");
        Directory.CreateDirectory(packageDir);
        var oldDocPath = Path.Combine(packageDir, "myeditor.document.toml");
        await File.WriteAllTextAsync(oldDocPath, "[document]\nid = \"my-doc\"\ntype = \"custom\"\ndisplay_name = \"My Doc\"\n\n[[document_file_types]]\nextension = \".my\"\ndisplay_name = \"My File\"\n");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(oldDocPath).Should().BeFalse();
        File.Exists(Path.Combine(packageDir, "myeditor.document.cel")).Should().BeTrue();
    }

    [Test]
    public async Task ApplyAsync_RewritesDocumentEditorsReferencesInPackageCel()
    {
        // Arrange
        WriteMinimalProjectFile();
        var packageDir = Path.Combine(_projectFolderPath, "packages", "my-package");
        Directory.CreateDirectory(packageDir);
        var oldManifestPath = Path.Combine(packageDir, "package.toml");
        await File.WriteAllTextAsync(oldManifestPath, """
            [package]
            id = "my-package"
            name = "My Package"
            version = "1.0.0"

            [contributes]
            document_editors = ["editor-a.document.toml", "editor-b.document.toml"]
            """);
        await File.WriteAllTextAsync(Path.Combine(packageDir, "editor-a.document.toml"), "[document]\nid = \"a\"\ntype = \"custom\"\ndisplay_name = \"A\"\n[[document_file_types]]\nextension = \".a\"\ndisplay_name = \"A\"\n");
        await File.WriteAllTextAsync(Path.Combine(packageDir, "editor-b.document.toml"), "[document]\nid = \"b\"\ntype = \"custom\"\ndisplay_name = \"B\"\n[[document_file_types]]\nextension = \".b\"\ndisplay_name = \"B\"\n");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var newManifestText = await File.ReadAllTextAsync(Path.Combine(packageDir, "package.cel"));
        newManifestText.Should().Contain("\"editor-a.document.cel\"");
        newManifestText.Should().Contain("\"editor-b.document.cel\"");
        newManifestText.Should().NotContain(".document.toml");
    }

    [Test]
    public async Task ApplyAsync_ConvertsWebViewFromJsonToToml()
    {
        // Arrange
        WriteMinimalProjectFile();
        var oldWebViewPath = Path.Combine(_projectFolderPath, "page.webview");
        await File.WriteAllTextAsync(oldWebViewPath, "{\"sourceUrl\": \"https://example.com/path\"}");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(oldWebViewPath).Should().BeFalse();

        var newPath = Path.Combine(_projectFolderPath, "page.webview.cel");
        File.Exists(newPath).Should().BeTrue();

        var newText = await File.ReadAllTextAsync(newPath);
        var parsed = Toml.Parse(newText);
        parsed.HasErrors.Should().BeFalse();

        var root = (TomlTable)parsed.ToModel();
        root.TryGetValue("source_url", out var urlValue).Should().BeTrue();
        urlValue.Should().Be("https://example.com/path");
    }

    [Test]
    public async Task ApplyAsync_ConvertsWebViewWithMissingSourceUrlToEmptyValue()
    {
        // Arrange
        WriteMinimalProjectFile();
        var oldWebViewPath = Path.Combine(_projectFolderPath, "empty.webview");
        await File.WriteAllTextAsync(oldWebViewPath, "{}");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var newPath = Path.Combine(_projectFolderPath, "empty.webview.cel");
        File.Exists(newPath).Should().BeTrue();

        var newText = await File.ReadAllTextAsync(newPath);
        newText.Should().Contain("source_url");
    }

    [Test]
    public async Task ApplyAsync_RewritesWebViewReferencesInProjectConfig()
    {
        // Arrange
        var content = """
            [celbridge]
            celbridge-version = "0.2.7"

            [project]
            name = "TestProject"
            entry = "Sites/index.webview"
            """;
        await File.WriteAllTextAsync(_projectFilePath, content);

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var updated = await File.ReadAllTextAsync(_projectFilePath);
        updated.Should().Contain("entry = \"Sites/index.webview.cel\"");
    }

    [Test]
    public async Task ApplyAsync_SkipsFilesInsideMetaDataFolder()
    {
        // Arrange
        WriteMinimalProjectFile();
        Directory.CreateDirectory(_projectDataFolderPath);

        var metadataWebView = Path.Combine(_projectDataFolderPath, "stale.webview");
        var metadataPackage = Path.Combine(_projectDataFolderPath, "package.toml");
        await File.WriteAllTextAsync(metadataWebView, "{}");
        await File.WriteAllTextAsync(metadataPackage, "[package]\nid = \"x\"\n");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(metadataWebView).Should().BeTrue();
        File.Exists(metadataPackage).Should().BeTrue();
        File.Exists(Path.Combine(_projectDataFolderPath, "stale.webview.cel")).Should().BeFalse();
        File.Exists(Path.Combine(_projectDataFolderPath, "package.cel")).Should().BeFalse();
    }

    [Test]
    public async Task ApplyAsync_IsIdempotent()
    {
        // Arrange
        WriteMinimalProjectFile();
        var packageDir = Path.Combine(_projectFolderPath, "packages", "my-package");
        Directory.CreateDirectory(packageDir);
        await File.WriteAllTextAsync(Path.Combine(packageDir, "package.toml"), "[package]\nid = \"my-package\"\nname = \"My Package\"\nversion = \"1.0.0\"\n");
        await File.WriteAllTextAsync(Path.Combine(_projectFolderPath, "page.webview"), "{\"sourceUrl\": \"https://example.com\"}");

        var context = CreateContext();

        // Act - run twice
        var firstResult = await _step.ApplyAsync(context);
        var secondResult = await _step.ApplyAsync(context);

        // Assert
        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(packageDir, "package.toml")).Should().BeFalse();
        File.Exists(Path.Combine(packageDir, "package.cel")).Should().BeTrue();
        File.Exists(Path.Combine(_projectFolderPath, "page.webview")).Should().BeFalse();
        File.Exists(Path.Combine(_projectFolderPath, "page.webview.cel")).Should().BeTrue();
    }

    private void WriteMinimalProjectFile()
    {
        var content = """
            [celbridge]
            celbridge-version = "0.2.7"

            [project]
            name = "TestProject"
            """;
        File.WriteAllText(_projectFilePath, content);
    }

    private MigrationContext CreateContext()
    {
        Func<string, Task<Result>> writeProjectFileAsync = async (text) =>
        {
            try
            {
                await File.WriteAllTextAsync(_projectFilePath, text);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail("Failed to write project file").WithException(ex);
            }
        };

        return new MigrationContext
        {
            ProjectFilePath = _projectFilePath,
            ProjectFolderPath = _projectFolderPath,
            ProjectDataFolderPath = _projectDataFolderPath,
            Configuration = new TomlTable(),
            Logger = _mockLogger,
            OriginalVersion = "0.2.7",
            WriteProjectFileAsync = writeProjectFileAsync,
        };
    }
}
