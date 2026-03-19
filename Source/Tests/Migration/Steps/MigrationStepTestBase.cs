using Celbridge.Logging;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;
using Tomlyn;

namespace Celbridge.Tests.Migration.Steps;

/// <summary>
/// Base class for migration step tests providing common test infrastructure.
/// Inherit from this class when creating tests for new migration steps.
/// </summary>
public abstract class MigrationStepTestBase<T> 
    where T : IMigrationStep, new()
{
    protected ILogger<MigrationContext> MockLogger { get; private set; } = null!;
    protected T MigrationStep { get; private set; } = default!;

    [SetUp]
    public virtual void SetUp()
    {
        MockLogger = MigrationTestHelper.CreateMockLogger<MigrationContext>();
        MigrationStep = new T();
    }

    /// <summary>
    /// Creates a temporary project file with the specified content.
    /// </summary>
    protected string CreateTempProjectFile(string content)
    {
        var tempPath = Path.GetTempFileName();
        var projectPath = Path.ChangeExtension(tempPath, ".celbridge");
        File.Delete(tempPath);
        File.WriteAllText(projectPath, content);
        return projectPath;
    }

    /// <summary>
    /// Creates a MigrationContext for testing.
    /// If originalVersion is not provided, attempts to extract it from the project file.
    /// Throws ArgumentException if originalVersion cannot be determined.
    /// </summary>
    protected async Task<MigrationContext> CreateMigrationContext(
        string projectFilePath,
        string? originalVersion = null)
    {
        var projectFolderPath = Path.GetDirectoryName(projectFilePath)!;
        var projectDataFolderPath = Path.Combine(projectFolderPath, "celbridge");

        var text = await File.ReadAllTextAsync(projectFilePath);
        var parse = Toml.Parse(text);
        var config = parse.ToModel();

        // If originalVersion not provided, try to extract from the file
        if (originalVersion == null)
        {
            originalVersion = MigrationTestHelper.ReadVersionFromFile(projectFilePath);
            
            if (originalVersion == null)
            {
                throw new ArgumentException(
                    $"Could not determine original version for test. " +
                    $"Either provide originalVersion parameter or ensure the project file contains a valid version. " +
                    $"File: {projectFilePath}");
            }
        }

        Func<string, Task<Result>> writeProjectFileAsync = async (content) =>
        {
            try
            {
                await File.WriteAllTextAsync(projectFilePath, content);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                return Result.Fail("Failed to write project file").WithException(ex);
            }
        };

        return new MigrationContext
        {
            ProjectFilePath = projectFilePath,
            ProjectFolderPath = projectFolderPath,
            ProjectDataFolderPath = projectDataFolderPath,
            Configuration = config,
            Logger = MockLogger,
            OriginalVersion = originalVersion,
            WriteProjectFileAsync = writeProjectFileAsync
        };
    }

    /// <summary>
    /// Verifies that the target version is set correctly.
    /// Override this in derived classes to specify the expected version.
    /// </summary>
    [Test]
    public virtual void TargetVersion_ShouldBeCorrect()
    {
        // This test should be overridden in derived classes to verify the specific version
        MigrationStep.TargetVersion.Should().NotBeNull();
        MigrationStep.TargetVersion.Should().BeGreaterThan(new Version("0.0.0"));
    }

    /// <summary>
    /// Verifies that applying the migration to an already-migrated file succeeds without changes.
    /// </summary>
    [Test]
    public virtual async Task ApplyAsync_AlreadyMigrated_SucceedsWithoutChanges()
    {
        // This is a general test that can be overridden for specific behavior
        // By default, migration steps should be idempotent
        await Task.CompletedTask;
    }

    [TearDown]
    public virtual void TearDown()
    {
        // Cleanup any resources
    }
}
