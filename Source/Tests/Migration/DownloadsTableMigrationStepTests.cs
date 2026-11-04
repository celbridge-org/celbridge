using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Tests DownloadsTableMigrationStep by running the migration service with the application's registered steps and
/// an application version that triggers the step.
/// </summary>
[TestFixture]
public class DownloadsTableMigrationStepTests
{
    private const string RecordedVersion = "1.6.0";
    private const string ApplicationVersion = "1.7.0";

    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private ProjectMigrationService _migrationService = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(DownloadsTableMigrationStepTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");

        var registry = new MigrationStepRegistry(MigrationTestHelper.CreateMockLogger<MigrationStepRegistry>());
        _migrationService = new ProjectMigrationService(
            MigrationTestHelper.CreateMockLogger<ProjectMigrationService>(),
            MigrationTestHelper.CreateMockEnvironmentService(ApplicationVersion),
            registry,
            new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task Upgrade_RemovesTheTableAndTheCommentAboveIt()
    {
        File.WriteAllText(_projectFilePath, $"""
            # Acme's project file
            [celbridge]
            celbridge-version = "{RecordedVersion}"
            description = "Acme"

            [celbridge.search]
            exclude = ["node_modules"]

            # Where files downloaded from web pages are saved
            [celbridge.downloads]
            folder = "assets/incoming"

            # Package folders and disabled packages
            [celbridge.packages]
            disabled = ["acme.unused"]
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);

        // The comment above the next table stays with that table, and the rest of the file stays the same.
        var projectFileText = ReadProjectFile();
        projectFileText.Should().Be($"""
            # Acme's project file
            [celbridge]
            celbridge-version = "{ApplicationVersion}"
            description = "Acme"

            [celbridge.search]
            exclude = ["node_modules"]

            # Package folders and disabled packages
            [celbridge.packages]
            disabled = ["acme.unused"]
            """);

        var parseResult = ProjectConfigParser.ParseFromText(projectFileText);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.EntryErrors.Should().BeEmpty();
    }

    [Test]
    public async Task Upgrade_TableWithOtherKeys_IsRemovedWhole()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [celbridge.downloads]
            folder = "assets/incoming"
            keep = [
                "acme.zip",
                "acme.pdf",
            ]

            [[shortcut]]
            resource = "readme.md"
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($"""
            [celbridge]
            celbridge-version = "{ApplicationVersion}"

            [[shortcut]]
            resource = "readme.md"
            """);
    }

    [Test]
    public async Task Upgrade_EmptyTableAtTheEnd_IsRemovedWithTheBlankLineAboveIt()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [celbridge.downloads]

            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($"""
            [celbridge]
            celbridge-version = "{ApplicationVersion}"

            """);
    }

    [Test]
    public async Task Upgrade_ProjectWithoutTheTable_ChangesOnlyTheVersion()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [celbridge.search]
            exclude = ["node_modules"]

            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($"""
            [celbridge]
            celbridge-version = "{ApplicationVersion}"

            [celbridge.search]
            exclude = ["node_modules"]

            """);
    }

    [Test]
    public async Task Upgrade_FromAnEarlierVersion_DropsTheDownloadsFolder()
    {
        File.WriteAllText(_projectFilePath, """
            [celbridge]
            celbridge-version = "1.5.0"

            [celbridge.resources]
            hide = [".gitignore"]
            downloads-folder = "assets/incoming"
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($"""
            [celbridge]
            celbridge-version = "{ApplicationVersion}"

            [celbridge.explorer]
            exclude = [".gitignore"]
            """);
    }

    // The migration writes the platform's line endings.
    private string ReadProjectFile()
    {
        return File.ReadAllText(_projectFilePath).ReplaceLineEndings("\n");
    }
}
