using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.MigrationSteps;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Tests.Migration.Steps;

/// <summary>
/// Unit tests for MigrationStep_0_3_0 which converts each pre-v0.3.0
/// "blah.webview" JSON file to "blah.webview.cel" TOML and rewrites quoted
/// references to the old extension in the project config.
/// </summary>
[TestFixture]
public class MigrationStep_0_3_0_Tests
{
    private ILogger<MigrationContext> _mockLogger = null!;
    private IFileSystem _fileSystem = null!;
    private MigrationStep_0_3_0 _step = null!;
    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private string _projectDataFolderPath = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<MigrationContext>();
        _fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());
        _step = new MigrationStep_0_3_0();

        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"MigrationStep_0_3_0_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectFolderPath);

        _projectFilePath = Path.Combine(_projectFolderPath, "test.celbridge");
        _projectDataFolderPath = Path.Combine(_projectFolderPath, LegacyConstants.MetaDataFolder);
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
    public async Task ApplyAsync_ConvertsWebViewFromJsonToToml()
    {
        WriteMinimalProjectFile();
        var oldWebViewPath = Path.Combine(_projectFolderPath, "page.webview");
        await File.WriteAllTextAsync(oldWebViewPath, "{\"sourceUrl\": \"https://example.com/path\"}");

        var context = CreateContext();

        var result = await _step.ApplyAsync(context);

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
        WriteMinimalProjectFile();
        var oldWebViewPath = Path.Combine(_projectFolderPath, "empty.webview");
        await File.WriteAllTextAsync(oldWebViewPath, "{}");

        var context = CreateContext();

        var result = await _step.ApplyAsync(context);

        result.IsSuccess.Should().BeTrue();
        var newPath = Path.Combine(_projectFolderPath, "empty.webview.cel");
        File.Exists(newPath).Should().BeTrue();

        var newText = await File.ReadAllTextAsync(newPath);
        newText.Should().Contain("source_url");
    }

    [Test]
    public async Task ApplyAsync_RewritesWebViewReferencesInProjectConfig()
    {
        var content = """
            [celbridge]
            celbridge-version = "0.2.7"

            [project]
            name = "TestProject"
            entry = "Sites/index.webview"
            """;
        await File.WriteAllTextAsync(_projectFilePath, content);

        var context = CreateContext();

        var result = await _step.ApplyAsync(context);

        result.IsSuccess.Should().BeTrue();
        var updated = await File.ReadAllTextAsync(_projectFilePath);
        updated.Should().Contain("entry = \"Sites/index.webview.cel\"");
    }

    [Test]
    public async Task ApplyAsync_LeavesTomlManifestsAlone()
    {
        // v0.3.0 keeps package.toml and *.document.toml on their original
        // extension. Only the .webview content file is touched.
        WriteMinimalProjectFile();
        var packageDir = Path.Combine(_projectFolderPath, "packages", "my-package");
        Directory.CreateDirectory(packageDir);

        var packagePath = Path.Combine(packageDir, "package.toml");
        var documentPath = Path.Combine(packageDir, "myeditor.document.toml");
        await File.WriteAllTextAsync(packagePath, "[package]\nid = \"my-package\"\n");
        await File.WriteAllTextAsync(documentPath, "[document]\nid = \"my-doc\"\n");

        var context = CreateContext();

        var result = await _step.ApplyAsync(context);

        result.IsSuccess.Should().BeTrue();
        File.Exists(packagePath).Should().BeTrue();
        File.Exists(documentPath).Should().BeTrue();
        File.Exists(Path.Combine(packageDir, "package.cel")).Should().BeFalse();
        File.Exists(Path.Combine(packageDir, "myeditor.document.cel")).Should().BeFalse();
    }

    [Test]
    public async Task ApplyAsync_SkipsFilesInsideMetaDataFolder()
    {
        WriteMinimalProjectFile();
        Directory.CreateDirectory(_projectDataFolderPath);

        var metadataWebView = Path.Combine(_projectDataFolderPath, "stale.webview");
        await File.WriteAllTextAsync(metadataWebView, "{}");

        var context = CreateContext();

        var result = await _step.ApplyAsync(context);

        result.IsSuccess.Should().BeTrue();
        File.Exists(metadataWebView).Should().BeTrue();
        File.Exists(Path.Combine(_projectDataFolderPath, "stale.webview.cel")).Should().BeFalse();
    }

    [Test]
    public async Task ApplyAsync_IsIdempotent()
    {
        WriteMinimalProjectFile();
        await File.WriteAllTextAsync(Path.Combine(_projectFolderPath, "page.webview"), "{\"sourceUrl\": \"https://example.com\"}");

        var context = CreateContext();

        var firstResult = await _step.ApplyAsync(context);
        var secondResult = await _step.ApplyAsync(context);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(_projectFolderPath, "page.webview")).Should().BeFalse();
        File.Exists(Path.Combine(_projectFolderPath, "page.webview.cel")).Should().BeTrue();
    }

    [Test]
    public async Task ApplyAsync_TreatsMalformedJsonAsEmptySourceUrl()
    {
        // A pre-0.3.0 .webview file with malformed JSON should not abort the
        // migration: the conversion treats the URL as empty and continues so
        // the file lands at the new extension and the user can supply a URL.
        WriteMinimalProjectFile();
        var oldWebViewPath = Path.Combine(_projectFolderPath, "broken.webview");
        await File.WriteAllTextAsync(oldWebViewPath, "{ not valid json");

        var context = CreateContext();

        var result = await _step.ApplyAsync(context);

        result.IsSuccess.Should().BeTrue();
        var newPath = Path.Combine(_projectFolderPath, "broken.webview.cel");
        File.Exists(newPath).Should().BeTrue();

        var newText = await File.ReadAllTextAsync(newPath);
        newText.Should().Contain("source_url = \"\"");
    }

    [Test]
    public async Task ApplyAsync_EscapesSpecialCharactersInSourceUrl()
    {
        // Quote and backslash characters in the sourceUrl must be escaped on
        // the TOML basic-string side or the resulting file fails to parse.
        WriteMinimalProjectFile();
        var oldWebViewPath = Path.Combine(_projectFolderPath, "tricky.webview");
        await File.WriteAllTextAsync(oldWebViewPath, "{\"sourceUrl\": \"https://example.com/q?x=\\\"a\\\"&y=back\\\\slash\"}");

        var context = CreateContext();

        var result = await _step.ApplyAsync(context);

        result.IsSuccess.Should().BeTrue();
        var newPath = Path.Combine(_projectFolderPath, "tricky.webview.cel");
        var newText = await File.ReadAllTextAsync(newPath);
        var parsed = Toml.Parse(newText);
        parsed.HasErrors.Should().BeFalse();

        var root = (TomlTable)parsed.ToModel();
        root.TryGetValue("source_url", out var urlValue).Should().BeTrue();
        urlValue.Should().Be("https://example.com/q?x=\"a\"&y=back\\slash");
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
            FileSystem = _fileSystem,
        };
    }
}
