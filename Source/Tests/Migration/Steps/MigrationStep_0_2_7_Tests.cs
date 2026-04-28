using Celbridge.Projects;
using Celbridge.Projects.MigrationSteps;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration.Steps;

/// <summary>
/// Unit tests for MigrationStep_0_2_7 which renames .webapp files to .webview and
/// rewrites cached references in the project TOML.
/// </summary>
[TestFixture]
public class MigrationStep_0_2_7_Tests
{
    private ILogger<MigrationContext> _mockLogger = null!;
    private MigrationStep_0_2_7 _step = null!;
    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private string _projectDataFolderPath = null!;

    [SetUp]
    public void SetUp()
    {
        _mockLogger = MigrationTestHelper.CreateMockLogger<MigrationContext>();
        _step = new MigrationStep_0_2_7();

        _projectFolderPath = Path.Combine(Path.GetTempPath(), $"MigrationStep_0_2_7_{Guid.NewGuid():N}");
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
    public void TargetVersion_IsZeroDotTwoDotSeven()
    {
        _step.TargetVersion.Should().Be(new Version("0.2.7"));
    }

    [Test]
    public async Task ApplyAsync_RenamesWebappFilesToWebview()
    {
        // Arrange
        WriteMinimalProjectFile();

        var rootWebapp = Path.Combine(_projectFolderPath, "page.webapp");
        var nestedWebapp = Path.Combine(_projectFolderPath, "Sites", "nested.webapp");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedWebapp)!);
        await File.WriteAllTextAsync(rootWebapp, "{\"sourceUrl\":\"https://example.com\"}");
        await File.WriteAllTextAsync(nestedWebapp, "{\"sourceUrl\":\"https://example.org\"}");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(rootWebapp).Should().BeFalse();
        File.Exists(nestedWebapp).Should().BeFalse();
        File.Exists(Path.ChangeExtension(rootWebapp, ".webview")).Should().BeTrue();
        File.Exists(Path.ChangeExtension(nestedWebapp, ".webview")).Should().BeTrue();
    }

    [Test]
    public async Task ApplyAsync_SkipsFilesInsideMetaDataFolder()
    {
        // Arrange
        WriteMinimalProjectFile();
        Directory.CreateDirectory(_projectDataFolderPath);

        var metadataWebapp = Path.Combine(_projectDataFolderPath, "stale.webapp");
        await File.WriteAllTextAsync(metadataWebapp, "{}");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(metadataWebapp).Should().BeTrue();
        File.Exists(Path.ChangeExtension(metadataWebapp, ".webview")).Should().BeFalse();
    }

    [Test]
    public async Task ApplyAsync_RewritesConfigTomlReferences()
    {
        // Arrange
        var content = """
            [celbridge]
            celbridge-version = "0.2.0"

            [project]
            name = "TestProject"
            entry = "Sites/index.webapp"
            """;
        await File.WriteAllTextAsync(_projectFilePath, content);

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();

        var updated = await File.ReadAllTextAsync(_projectFilePath);
        updated.Should().Contain("entry = \"Sites/index.webview\"");
        updated.Should().NotContain(".webapp");
    }

    [Test]
    public async Task ApplyAsync_LeavesUnrelatedFilesUntouched()
    {
        // Arrange
        WriteMinimalProjectFile();

        var textFile = Path.Combine(_projectFolderPath, "notes.txt");
        var pythonFile = Path.Combine(_projectFolderPath, "script.py");
        var htmlFile = Path.Combine(_projectFolderPath, "page.html");
        await File.WriteAllTextAsync(textFile, "Mentions a .webapp path in prose.");
        await File.WriteAllTextAsync(pythonFile, "# script.py — references foo.webapp in a comment");
        await File.WriteAllTextAsync(htmlFile, "<html><body>Hello</body></html>");

        var context = CreateContext();

        // Act
        var result = await _step.ApplyAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(textFile).Should().BeTrue();
        File.Exists(pythonFile).Should().BeTrue();
        File.Exists(htmlFile).Should().BeTrue();

        (await File.ReadAllTextAsync(textFile)).Should().Contain(".webapp");
        (await File.ReadAllTextAsync(pythonFile)).Should().Contain(".webapp");
    }

    [Test]
    public async Task ApplyAsync_IsIdempotent()
    {
        // Arrange
        WriteMinimalProjectFile();

        var rootWebapp = Path.Combine(_projectFolderPath, "page.webapp");
        await File.WriteAllTextAsync(rootWebapp, "{}");

        var context = CreateContext();

        // Act — run twice
        var firstResult = await _step.ApplyAsync(context);
        var secondResult = await _step.ApplyAsync(context);

        // Assert
        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();

        File.Exists(rootWebapp).Should().BeFalse();
        File.Exists(Path.ChangeExtension(rootWebapp, ".webview")).Should().BeTrue();
    }

    private void WriteMinimalProjectFile()
    {
        var content = """
            [celbridge]
            celbridge-version = "0.2.0"

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
            Configuration = new Tomlyn.Model.TomlTable(),
            Logger = _mockLogger,
            OriginalVersion = "0.2.0",
            WriteProjectFileAsync = writeProjectFileAsync,
        };
    }
}
