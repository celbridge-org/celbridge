using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Tests PythonFeatureMigrationStep by running the migration service with the application's registered steps and
/// an application version that triggers the step.
/// </summary>
[TestFixture]
public class PythonFeatureMigrationStepTests
{
    private const string RecordedVersion = "1.7.0";
    private const string ApplicationVersion = "1.8.0";

    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private ProjectMigrationService _migrationService = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(PythonFeatureMigrationStepTests),
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
    public async Task Upgrade_NoFeaturesTable_AddsOneAfterTheLastCelbridgeKey()
    {
        File.WriteAllText(_projectFilePath, $$"""
            [celbridge]
            celbridge-version = "{{RecordedVersion}}"
            description = "Acme"

            # Where the Explorer hides files
            [celbridge.explorer]
            exclude = [".gitignore"]
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);

        var projectFileText = ReadProjectFile();
        projectFileText.Should().Be($$"""
            [celbridge]
            celbridge-version = "{{ApplicationVersion}}"
            description = "Acme"
            features = { python = true }

            # Where the Explorer hides files
            [celbridge.explorer]
            exclude = [".gitignore"]
            """);

        var parseResult = ProjectConfigParser.ParseFromText(projectFileText);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.EntryErrors.Should().BeEmpty();
        parseResult.Value.Features.Should().Contain("python", true);
    }

    [Test]
    public async Task Upgrade_InlineFeaturesTable_KeepsItsEntries()
    {
        File.WriteAllText(_projectFilePath, $$"""
            [celbridge]
            celbridge-version = "{{RecordedVersion}}"
            features = { "note-editor" = true, "webview-dev-tools" = false }
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($$"""
            [celbridge]
            celbridge-version = "{{ApplicationVersion}}"
            features = { "note-editor" = true, "webview-dev-tools" = false, python = true }
            """);
    }

    [Test]
    public async Task Upgrade_FeaturesTableUnderItsOwnHeader_GainsTheFeature()
    {
        File.WriteAllText(_projectFilePath, $$"""
            [celbridge]
            celbridge-version = "{{RecordedVersion}}"

            [celbridge.features]
            note-editor = true
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($$"""
            [celbridge]
            celbridge-version = "{{ApplicationVersion}}"

            [celbridge.features]
            python = true
            note-editor = true
            """);
    }

    [Test]
    public async Task Upgrade_FeaturesWrittenAsDottedKeys_GainsTheFeatureAfterTheLastOne()
    {
        File.WriteAllText(_projectFilePath, $$"""
            [celbridge]
            celbridge-version = "{{RecordedVersion}}"
            features.note-editor = true
            features . "webview-dev-tools" = false
            description = "Acme"

            [celbridge.explorer]
            exclude = [".gitignore"]
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);

        var projectFileText = ReadProjectFile();
        projectFileText.Should().Be($$"""
            [celbridge]
            celbridge-version = "{{ApplicationVersion}}"
            features.note-editor = true
            features . "webview-dev-tools" = false
            features.python = true
            description = "Acme"

            [celbridge.explorer]
            exclude = [".gitignore"]
            """);

        var parseResult = ProjectConfigParser.ParseFromText(projectFileText);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.EntryErrors.Should().BeEmpty();
        parseResult.Value.Features.Should().Contain("python", true);
        parseResult.Value.Features.Should().Contain("note-editor", true);
    }

    [Test]
    public async Task Upgrade_ProjectThatNamesThePythonFeature_ChangesOnlyTheVersion()
    {
        File.WriteAllText(_projectFilePath, $$"""
            [celbridge]
            celbridge-version = "{{RecordedVersion}}"
            features = { python = false }
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($$"""
            [celbridge]
            celbridge-version = "{{ApplicationVersion}}"
            features = { python = false }
            """);
    }

    // The migration writes the platform's line endings.
    private string ReadProjectFile()
    {
        return File.ReadAllText(_projectFilePath).ReplaceLineEndings("\n");
    }
}
