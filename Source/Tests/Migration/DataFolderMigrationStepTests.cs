using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Tests DataFolderMigrationStep by running the migration service with the application's registered steps and
/// an application version that triggers the step.
/// </summary>
[TestFixture]
public class DataFolderMigrationStepTests
{
    private const string RecordedVersion = "1.4.0";
    private const string ApplicationVersion = "1.5.0";

    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private string _celbridgeFolderPath = null!;
    private ProjectMigrationService _migrationService = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(DataFolderMigrationStepTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");
        File.WriteAllText(_projectFilePath, $"""
            # Acme's project file
            [celbridge]
            celbridge-version = "{RecordedVersion}"
            data-folder = "variant-a"
            description = "Acme"

            [celbridge.resources]
            hide = ["*.log"]
            """);

        _celbridgeFolderPath = Path.Combine(_projectFolderPath, ".celbridge");
        var settingsFolderPath = Path.Combine(_celbridgeFolderPath, "variant-a", "settings");
        Directory.CreateDirectory(settingsFolderPath);
        File.WriteAllText(Path.Combine(settingsFolderPath, "workspace_settings.json"), "{}");

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
    public async Task Upgrade_DeletesTheCelbridgeFolderAndTheDataFolderKey()
    {
        var checkResult = await _migrationService.CheckMigrationAsync(_projectFilePath);
        checkResult.Status.Should().Be(MigrationStatus.UpgradeRequired);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        Directory.Exists(_celbridgeFolderPath).Should().BeFalse();

        // Only the key's line is removed. The rest of the file is unchanged.
        var projectFileText = File.ReadAllText(_projectFilePath);
        projectFileText.Should().NotContain("data-folder");
        projectFileText.Should().Contain("# Acme's project file");
        projectFileText.Should().Contain("description = \"Acme\"");
        projectFileText.Should().Contain("hide = [\"*.log\"]");
        MigrationTestHelper.ReadVersionFromFile(_projectFilePath).Should().Be(ApplicationVersion);

        var parseResult = ProjectConfigParser.ParseFromText(projectFileText);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.EntryErrors.Should().BeEmpty();
    }

    [Test]
    public async Task Upgrade_RunAgain_ChangesNothing()
    {
        await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);
        var migratedText = File.ReadAllText(_projectFilePath);

        // A development build can create a .celbridge folder in a project that already records this version.
        // That's an ordinary folder, so a second run must not delete it.
        Directory.CreateDirectory(_celbridgeFolderPath);

        var checkResult = await _migrationService.CheckMigrationAsync(_projectFilePath);
        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        checkResult.Status.Should().Be(MigrationStatus.Complete);
        upgradeResult.Status.Should().Be(MigrationStatus.Complete);
        File.ReadAllText(_projectFilePath).Should().Be(migratedText);
        Directory.Exists(_celbridgeFolderPath).Should().BeTrue();
    }

    [Test]
    public async Task Upgrade_ProjectWithoutTheKeyOrTheFolder_KeepsItsFileAsItWas()
    {
        Directory.Delete(_celbridgeFolderPath, recursive: true);
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        File.ReadAllText(_projectFilePath).Should().Contain($"celbridge-version = \"{ApplicationVersion}\"");
    }
}
