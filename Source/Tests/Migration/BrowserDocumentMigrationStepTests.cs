using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.WebHost;

namespace Celbridge.Tests.Migration;

/// <summary>
/// Tests BrowserDocumentMigrationStep by running the migration service with the application's registered steps and an
/// application version that triggers the step.
/// </summary>
[TestFixture]
public class BrowserDocumentMigrationStepTests
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
            nameof(BrowserDocumentMigrationStepTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _projectFilePath = Path.Combine(_projectFolderPath, "Acme.celbridge");
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"
            """);

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

    private string ProjectPath(string relativePath)
    {
        return Path.Combine(_projectFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private void WriteProjectFile(string relativePath, string content)
    {
        var path = ProjectPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task UpgradeAsync()
    {
        var upgradeResult = await _migrationService.PerformMigrationUpgradeAsync(_projectFilePath);

        upgradeResult.Status.Should().Be(MigrationStatus.Complete, upgradeResult.OperationResult.MessageChain);
    }

    [Test]
    public async Task Upgrade_MovesADocumentAndItsSidecar()
    {
        WriteProjectFile("references/page.webview", "[[bookmarks]]\nurl = \"https://example.com/\"\n");
        WriteProjectFile("references/page.webview.cel", "author = \"Acme\"\n");

        await UpgradeAsync();

        File.Exists(ProjectPath("references/page.webview")).Should().BeFalse();
        File.Exists(ProjectPath("references/page.webview.cel")).Should().BeFalse();
        File.ReadAllText(ProjectPath("references/page.browser")).Should().Be("[[bookmarks]]\nurl = \"https://example.com/\"\n");
        File.ReadAllText(ProjectPath("references/page.browser.cel")).Should().Be("author = \"Acme\"\n");
    }

    [Test]
    public async Task Upgrade_SkipsTheGitFolder()
    {
        WriteProjectFile(".git/page.webview", string.Empty);

        await UpgradeAsync();

        File.Exists(ProjectPath(".git/page.webview")).Should().BeTrue();
    }

    [Test]
    public async Task Upgrade_LeavesADocumentWhenABrowserFileIsAtThePath()
    {
        WriteProjectFile("page.webview", "# old\n");
        WriteProjectFile("page.webview.cel", "author = \"Old\"\n");
        WriteProjectFile("page.browser", "# new\n");
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [[shortcut]]
            resource = "page.webview"
            """);

        await UpgradeAsync();

        File.ReadAllText(ProjectPath("page.webview")).Should().Be("# old\n");
        File.ReadAllText(ProjectPath("page.webview.cel")).Should().Be("author = \"Old\"\n");
        File.ReadAllText(ProjectPath("page.browser")).Should().Be("# new\n");
        File.ReadAllText(_projectFilePath).Should().Contain("resource = \"page.webview\"");
    }

    [Test]
    public async Task Upgrade_RenamesAShortcutAndLeavesTheRestOfTheProjectFile()
    {
        WriteProjectFile("docs/page.webview", string.Empty);
        File.WriteAllText(_projectFilePath, $"""
            # Acme's project file
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [[shortcut]]
            resource = "project:docs/page.webview" # the docs
            icon = "bs-book"

            [[shortcut]]
            resource = "notes.md"

            [celbridge.search]
            resource = "other.webview"
            """);

        await UpgradeAsync();

        File.ReadAllText(_projectFilePath).Should().Be($"""
            # Acme's project file
            [celbridge]
            celbridge-version = "{ApplicationVersion}"

            [[shortcut]]
            resource = "project:docs/page.browser" # the docs
            icon = "bs-book"

            [[shortcut]]
            resource = "notes.md"

            [celbridge.search]
            resource = "other.webview"
            """);
    }

    // The step moves only files in the project folder. A shortcut on another root, or to a file that is not there,
    // goes on naming the old file.
    [Test]
    public async Task Upgrade_LeavesAShortcutToAFileTheStepDidNotMove()
    {
        File.WriteAllText(_projectFilePath, $"""
            [celbridge]
            celbridge-version = "{RecordedVersion}"

            [[shortcut]]
            resource = "utils:scratch.webview"

            [[shortcut]]
            resource = "missing.webview"
            """);

        await UpgradeAsync();

        var projectFile = File.ReadAllText(_projectFilePath);
        projectFile.Should().Contain("resource = \"utils:scratch.webview\"");
        projectFile.Should().Contain("resource = \"missing.webview\"");
    }

    private async Task<BrowserFileContent> UpgradeAndReadAsync(string oldContents)
    {
        WriteProjectFile("page.webview", oldContents);

        await UpgradeAsync();

        var parseResult = BrowserFileContent.TryParse(File.ReadAllText(ProjectPath("page.browser")));
        parseResult.IsSuccess.Should().BeTrue(parseResult.FirstErrorMessage);

        return parseResult.Value;
    }

    [Test]
    public async Task Upgrade_TurnsTheSourceUrlIntoAnOpenOnStartBookmarkAtTheTop()
    {
        var content = await UpgradeAndReadAsync("""
            source_url = "https://www.example.com/start"

            [[bookmarks]]
            url = "https://example.com/docs"
            name = "Docs"
            """);

        content.UnknownFields.Should().BeEmpty();
        content.Bookmarks.Should().Equal(
            new BrowserBookmark("https://www.example.com/start", "www.example.com", OpenOnStart: true),
            new BrowserBookmark("https://example.com/docs", "Docs"));
    }

    [Test]
    public async Task Upgrade_TurnsOnABookmarkThatMatchesTheSourceUrlAcrossATrailingSlash()
    {
        var content = await UpgradeAndReadAsync("""
            source_url = "https://example.com"

            [[bookmarks]]
            url = "https://example.com/docs"

            [[bookmarks]]
            url = "https://example.com/"
            name = "Home"
            """);

        content.Bookmarks.Should().Equal(
            new BrowserBookmark("https://example.com/docs"),
            new BrowserBookmark("https://example.com/", "Home", OpenOnStart: true));
    }

    [Test]
    public async Task Upgrade_AddsASchemeToASourceUrlWithNone()
    {
        var content = await UpgradeAndReadAsync("""source_url = "example.com/start" """);

        content.Bookmarks.Should().Equal(new BrowserBookmark("https://example.com/start", "example.com", OpenOnStart: true));
    }

    [Test]
    public async Task Upgrade_AddsNoBookmarkForABlankSourceUrl()
    {
        var content = await UpgradeAndReadAsync("""
            # Opens a web page in an embedded browser.
            source_url = ""
            """);

        content.Bookmarks.Should().BeEmpty();
        content.UnknownFields.Should().BeEmpty();
    }

    [Test]
    public async Task Upgrade_RemovesTheToolbarKeysAndBookmarkIcons()
    {
        var content = await UpgradeAndReadAsync("""
            show_url_bar = false
            show_bookmarks_bar = false

            [[bookmarks]]
            url = "https://example.com/docs"
            name = "Docs"
            icon = "bs-book"
            """);

        content.UnknownFields.Should().BeEmpty();
        content.Bookmarks.Should().Equal(new BrowserBookmark("https://example.com/docs", "Docs"));
    }

    [Test]
    public async Task Upgrade_KeepsTheContentsOfAFileWithNoneOfTheOldKeys()
    {
        const string Contents = """
            # Team bookmarks
            [[bookmarks]]
            url = "https://example.com/docs"
            """;

        WriteProjectFile("page.webview", Contents);

        await UpgradeAsync();

        File.ReadAllText(ProjectPath("page.browser")).Should().Be(Contents);
    }

    [Test]
    public async Task Upgrade_KeepsTheContentsOfAFileThatFailsToParse()
    {
        const string Contents = "source_url = not quoted";

        WriteProjectFile("page.webview", Contents);

        await UpgradeAsync();

        File.ReadAllText(ProjectPath("page.browser")).Should().Be(Contents);
    }
}
