using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Tests ResourceSettingsMigrationStep by running the migration service with the application's registered steps
/// and an application version that triggers the step.
/// </summary>
[TestFixture]
public class ResourceSettingsMigrationStepTests
{
    private const string RecordedVersion = "1.5.0";
    private const string ApplicationVersion = "1.6.0";

    private string _projectFolderPath = null!;
    private string _projectFilePath = null!;
    private ProjectMigrationService _migrationService = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceSettingsMigrationStepTests),
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
    public async Task Upgrade_MovesEverySettingToItsNewTable()
    {
        File.WriteAllText(_projectFilePath, $"""
            # Acme's project file
            [celbridge]
            celbridge-version = "{RecordedVersion}"
            disabled-packages = ["acme.unused"]
            description = "Acme"

            # 'hide' keeps resources out of the Explorer tree. 'search-exclude' also keeps them out of search.
            # Both take glob patterns, e.g. "Python/.venv/**".
            [celbridge.resources]
            hide = [".gitignore", ".DS_Store"]
            search-exclude = ["node_modules", "Python/.venv/**"]
            downloads-folder = "assets/incoming"

            [[shortcut]]
            resource = "readme.md"
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);

        // The migration removes the comment above the old table and leaves the rest of the file unchanged.
        var projectFileText = ReadProjectFile();
        projectFileText.Should().Be($"""
            # Acme's project file
            [celbridge]
            celbridge-version = "{ApplicationVersion}"
            description = "Acme"

            [celbridge.explorer]
            exclude = [".gitignore", ".DS_Store"]

            [celbridge.search]
            exclude = ["node_modules", "Python/.venv/**"]

            [celbridge.downloads]
            folder = "assets/incoming"

            [celbridge.packages]
            disabled = ["acme.unused"]

            [[shortcut]]
            resource = "readme.md"
            """);

        var parseResult = ProjectConfigParser.ParseFromText(projectFileText);
        parseResult.IsSuccess.Should().BeTrue();
        var config = parseResult.Value;
        config.EntryErrors.Should().BeEmpty();
        config.Explorer.Exclude.Should().Equal(".gitignore", ".DS_Store");
        config.Search.Exclude.Should().Equal("node_modules", "Python/.venv/**");
        config.Downloads.Folder.Should().Be("assets/incoming");
        config.Packages.Disabled.Should().Equal("acme.unused");
    }

    [Test]
    public async Task Upgrade_ProjectWithTheNewTable_KeepsItAndDropsTheOldSetting()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [celbridge.resources]
            hide = [".gitignore"]
            search-exclude = ["node_modules"]

            [celbridge.explorer]
            exclude = ["*.log"]
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);

        var parseResult = ProjectConfigParser.ParseFromText(ReadProjectFile());
        parseResult.IsSuccess.Should().BeTrue();
        var config = parseResult.Value;
        config.EntryErrors.Should().BeEmpty();
        config.Explorer.Exclude.Should().Equal("*.log");
        config.Search.Exclude.Should().Equal("node_modules");
    }

    [Test]
    public async Task Upgrade_DisabledPackagesOverSeveralLines_MovesTheWholeList()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"
            disabled-packages = [
                "acme.notes",
                "acme.unused",
            ]

            [[shortcut]]
            resource = "readme.md"
            """);

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
        ReadProjectFile().Should().Be($"""
            [celbridge]
            celbridge-version = "{ApplicationVersion}"

            [celbridge.packages]
            disabled = ["acme.notes", "acme.unused"]

            [[shortcut]]
            resource = "readme.md"
            """);
    }

    [Test]
    public async Task Upgrade_PackagesOutsideThePackagesFolder_ListsTheFoldersThatContainThem()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"
            disabled-packages = ["acme.unused"]
            """);

        CreatePackage("tools/acme-sound");
        CreatePackage("tools/acme-sprite");
        CreatePackage("packages/group/acme-board");

        // Loading the project reads the packages folder without an entry, and drops the project root as an entry.
        // The search skips the .git folder and the folders inside a package.
        CreatePackage("packages/acme-notes");
        CreatePackage("acme-tool");
        CreatePackage(".git/acme-hidden");
        CreatePackage("tools/acme-sprite/vendor/acme-inner");

        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);

        var projectFileText = ReadProjectFile();
        projectFileText.Should().Be($"""
            [celbridge]
            celbridge-version = "{ApplicationVersion}"

            [celbridge.packages]
            folders = ["packages/group", "tools"]
            disabled = ["acme.unused"]
            """);

        var parseResult = ProjectConfigParser.ParseFromText(projectFileText);
        parseResult.IsSuccess.Should().BeTrue();
        var config = parseResult.Value;
        config.EntryErrors.Should().BeEmpty();
        config.Packages.Folders.Should().Equal("packages/group", "tools");
        config.Packages.Disabled.Should().Equal("acme.unused");
    }

    [Test]
    public async Task Upgrade_ProjectWithoutTheOldSettings_ChangesOnlyTheVersion()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [celbridge.search]
            exclude = ["node_modules"]

            """);
        CreatePackage("packages/acme-notes");

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
    public async Task Upgrade_RunAgain_ChangesNothing()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [celbridge.resources]
            hide = [".gitignore"]

            """);

        await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);
        var migratedText = ReadProjectFile();

        var checkResult = await _migrationService.CheckMigrationAsync(_projectFilePath);
        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        checkResult.Status.Should().Be(MigrationStatus.Complete);
        upgradeResult.Status.Should().Be(MigrationStatus.Complete);
        ReadProjectFile().Should().Be(migratedText);
        migratedText.Should().Be($"""
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

    private void CreatePackage(string packagePath)
    {
        var packageFolderPath = Path.Combine(_projectFolderPath, packagePath);
        Directory.CreateDirectory(packageFolderPath);
        File.WriteAllText(Path.Combine(packageFolderPath, "package.toml"), string.Empty);
    }
}
