using Celbridge.Projects.MigrationSteps;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration.Steps;

/// <summary>
/// Unit tests for MigrationStep_0_1_5 which converts legacy version format to celbridge-version format.
/// </summary>
[TestFixture]
public class MigrationStep_0_1_5_Tests : MigrationStepTestBase<MigrationStep_0_1_5>
{
    #region Target Version Tests

    [Test]
    public override void TargetVersion_ShouldBeCorrect()
    {
        MigrationStep.TargetVersion.Should().Be(new Version("0.1.5"));
    }

    #endregion

    #region Legacy Format Migration Tests

    [Test]
    public async Task ApplyAsync_LegacyVersionProperty_ConvertsToNewFormat()
    {
        // Arrange
        var originalContent = """
            [celbridge]
            version = "0.1.4"

            [project]
            name = "TestProject"
            """;

        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.1.4");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var updatedContent = File.ReadAllText(projectPath);
            updatedContent.Should().Contain("celbridge-version = \"0.1.5\"");
            updatedContent.Should().NotContain("version = \"0.1.4\"");
            updatedContent.Should().Contain("[project]");
            updatedContent.Should().Contain("name = \"TestProject\"");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task ApplyAsync_Legacy4PartVersion_ConvertsToNewFormat()
    {
        // Arrange
        var originalContent = """
            [celbridge]
            version = "0.1.4.2"

            [project]
            name = "TestProject"
            """;

        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.1.4.2");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var updatedContent = File.ReadAllText(projectPath);
            updatedContent.Should().Contain("celbridge-version = \"0.1.5\"");
            updatedContent.Should().NotContain("version = \"0.1.4.2\"");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task ApplyAsync_LegacyVersionWithWhitespace_PreservesFormatting()
    {
        // Arrange
        var originalContent = """
            [celbridge]
                version = "0.1.4"

            [project]
            name = "TestProject"
            """;

        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.1.4");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var updatedContent = File.ReadAllText(projectPath);
            updatedContent.Should().Contain("[celbridge]");
            updatedContent.Should().Contain("celbridge-version = \"0.1.5\"");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region No-Op Tests

    [Test]
    public async Task ApplyAsync_NoLegacyVersion_DoesNotModifyFile()
    {
        // Arrange
        var originalContent = """
            [celbridge]
            celbridge-version = "0.1.5"

            [project]
            name = "TestProject"
            """;

        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.1.5");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var updatedContent = File.ReadAllText(projectPath);
            updatedContent.Should().Be(originalContent);
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task ApplyAsync_EmptyFile_DoesNotFail()
    {
        // Arrange
        var originalContent = "";
        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.0.0");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Line Ending Tests

    [Test]
    public async Task ApplyAsync_WindowsLineEndings_PreservesFormat()
    {
        // Arrange
        var originalContent = "[celbridge]\r\nversion = \"0.1.4\"\r\n\r\n[project]\r\nname = \"TestProject\"";
        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.1.4");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var updatedContent = File.ReadAllText(projectPath);
            updatedContent.Should().Contain("celbridge-version = \"0.1.5\"");
            updatedContent.Should().NotContain("version = \"0.1.4\"");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    [Test]
    public async Task ApplyAsync_UnixLineEndings_PreservesFormat()
    {
        // Arrange
        var originalContent = "[celbridge]\nversion = \"0.1.4\"\n\n[project]\nname = \"TestProject\"";
        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.1.4");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var updatedContent = File.ReadAllText(projectPath);
            updatedContent.Should().Contain("celbridge-version = \"0.1.5\"");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion

    #region Complex File Tests

    [Test]
    public async Task ApplyAsync_ComplexFile_PreservesOtherContent()
    {
        // Arrange
        var originalContent = """
            [celbridge]
            version = "0.1.4"

            [project]
            name = "TestProject"
            version = "1.0.0"
            requires-python = "3.12"
            dependencies = ["package1", "package2"]

            [other-section]
            key1 = "value1"
            key2 = 42
            """;

        var projectPath = CreateTempProjectFile(originalContent);

        try
        {
            var context = await CreateMigrationContext(projectPath, "0.1.4");

            // Act
            var result = await MigrationStep.ApplyAsync(context);

            // Assert
            result.IsSuccess.Should().BeTrue();

            var updatedContent = File.ReadAllText(projectPath);
            
            // Check migration occurred
            updatedContent.Should().Contain("celbridge-version = \"0.1.5\"");
            updatedContent.Should().NotContain("[celbridge]\nversion = \"0.1.4\"");
            
            // Check other content preserved
            updatedContent.Should().Contain("[project]");
            updatedContent.Should().Contain("name = \"TestProject\"");
            updatedContent.Should().Contain("version = \"1.0.0\"");
            updatedContent.Should().Contain("requires-python = \"3.12\"");
            updatedContent.Should().Contain("dependencies = [\"package1\", \"package2\"]");
            updatedContent.Should().Contain("[other-section]");
            updatedContent.Should().Contain("key1 = \"value1\"");
            updatedContent.Should().Contain("key2 = 42");
        }
        finally
        {
            MigrationTestHelper.CleanupTempFile(projectPath);
        }
    }

    #endregion
}
