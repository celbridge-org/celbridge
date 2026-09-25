using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Python;
using Celbridge.Tests.FileSystem;
using Celbridge.Utilities;
using Celbridge.Utilities.Platform;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Projects;

[TestFixture]
public class ProjectTemplateServiceTests
{
    private ILocalFileSystem _fileSystem = null!;
    private ProjectTemplateService _projectTemplateService = null!;
    private string _tempRootPath = null!;
    private string _expectedAppVersion = null!;

    [SetUp]
    public void Setup()
    {
        _fileSystem = TestFileSystem.CreateLocal();

        // The localizer is only used to label the templates, not by the creation flow.
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        var logger = Substitute.For<ILogger<ProjectTemplateService>>();

        // A real app environment so the test exercises actual bundled-asset path resolution (the
        // AppContext.BaseDirectory layout the Skia heads use) and supplies the temp folder. Its reported
        // version is what should flow into the generated project file.
        var appEnvironment = new AppEnvironment();
        _expectedAppVersion = appEnvironment.GetEnvironmentInfo().AppVersion;

        _projectTemplateService = new ProjectTemplateService(
            stringLocalizer,
            _fileSystem,
            appEnvironment,
            logger);

        _tempRootPath = Path.Combine(
            Path.GetTempPath(),
            "celbridge-ws6-template-tests",
            Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));
        Directory.CreateDirectory(_tempRootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRootPath))
        {
            Directory.Delete(_tempRootPath, recursive: true);
        }
    }

    [Test]
    public async Task CreateFromTemplate_Empty_CreatesProjectFromBundledZip()
    {
        // Exercises the non-Windows (#else) asset path that macOS runs: the template zip is read from
        // AppContext.BaseDirectory rather than ms-appx, then extracted, version-substituted, and moved
        // into the destination folder.
        var template = _projectTemplateService.GetDefaultTemplate();
        template.Id.Should().Be("Empty");

        var projectFolderPath = Path.Combine(_tempRootPath, "MyProject");
        var projectFilePath = Path.Combine(projectFolderPath, "MyProject.celbridge");

        var result = await _projectTemplateService.CreateFromTemplateAsync(projectFilePath, template);

        result.IsSuccess.Should().BeTrue();

        // The renamed project file and the template's bundled files land in the project folder.
        File.Exists(projectFilePath).Should().BeTrue();
        File.Exists(Path.Combine(projectFolderPath, "readme.md")).Should().BeTrue();

        // The version placeholder in the template's project file is substituted from the environment.
        var projectContents = await File.ReadAllTextAsync(projectFilePath);
        projectContents.Should().NotContain("<application-version>");

        // The project records the Celbridge version that created it and sets no project version, so it has
        // the default version until the user sets one.
        var parseResult = ProjectConfigParser.ParseFromText(projectContents);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.Celbridge.CelbridgeVersion.Should().Be(_expectedAppVersion);
        parseResult.Value.Celbridge.ProjectVersion.Should().BeNull();
    }

    [Test]
    public async Task CreateFromTemplate_Python_CreatesProjectWithStarterFiles()
    {
        var template = _projectTemplateService.GetTemplates().Single(t => t.Id == "Python");

        var projectFolderPath = Path.Combine(_tempRootPath, "MyPythonProject");
        var projectFilePath = Path.Combine(projectFolderPath, "MyPythonProject.celbridge");

        var result = await _projectTemplateService.CreateFromTemplateAsync(projectFilePath, template);

        result.IsSuccess.Should().BeTrue();

        File.Exists(projectFilePath).Should().BeTrue();
        File.Exists(Path.Combine(projectFolderPath, "project.celbridge")).Should().BeFalse();
        File.Exists(Path.Combine(projectFolderPath, "readme.md")).Should().BeTrue();
        File.Exists(Path.Combine(projectFolderPath, "hello_world.py")).Should().BeTrue();
        File.Exists(Path.Combine(projectFolderPath, "python.console")).Should().BeTrue();

        var projectContents = await File.ReadAllTextAsync(projectFilePath);
        projectContents.Should().NotContain("<application-version>");

        // The project file parses cleanly under the current schema, sets no project version, and declares
        // the console shortcut.
        var parseResult = ProjectConfigParser.ParseFromText(projectContents);
        parseResult.IsSuccess.Should().BeTrue();
        parseResult.Value.EntryErrors.Should().BeEmpty();
        parseResult.Value.Celbridge.ProjectVersion.Should().BeNull();
        parseResult.Value.DocumentShortcuts.Should().ContainSingle(s => s.Resource == "python.console");
    }

    [Test]
    public async Task CreateFromTemplate_ExistingTemplateFile_ReplacesIt()
    {
        // Creating a project in a folder that already holds one of the template's files used to fail
        // partway through the move, leaving a half-created project behind (issue #987). The template
        // owns its files, so the existing one is replaced and creation succeeds.
        var template = _projectTemplateService.GetDefaultTemplate();

        var projectFolderPath = Path.Combine(_tempRootPath, "ExistingFolder");
        Directory.CreateDirectory(projectFolderPath);
        var readmePath = Path.Combine(projectFolderPath, "readme.md");
        await File.WriteAllTextAsync(readmePath, "the user's own readme");

        var projectFilePath = Path.Combine(projectFolderPath, "MyProject.celbridge");

        var result = await _projectTemplateService.CreateFromTemplateAsync(projectFilePath, template);

        result.IsSuccess.Should().BeTrue();
        File.Exists(projectFilePath).Should().BeTrue();

        var readmeContents = await File.ReadAllTextAsync(readmePath);
        readmeContents.Should().NotContain("the user's own readme");
    }

    [Test]
    public async Task GetConflictingFileNames_ReportsFilesTheTemplateWouldReplace()
    {
        var template = _projectTemplateService.GetTemplates().Single(t => t.Id == "Python");

        var projectFolderPath = Path.Combine(_tempRootPath, "ExistingFolder");
        Directory.CreateDirectory(projectFolderPath);
        await File.WriteAllTextAsync(Path.Combine(projectFolderPath, "readme.md"), "user content");
        await File.WriteAllTextAsync(Path.Combine(projectFolderPath, "hello_world.py"), "user content");
        await File.WriteAllTextAsync(Path.Combine(projectFolderPath, "untouched.txt"), "user content");

        // The template's .gitignore is merged rather than replaced, so it is never a conflict.
        await File.WriteAllTextAsync(Path.Combine(projectFolderPath, ".gitignore"), "*.log\n");

        var projectFilePath = Path.Combine(projectFolderPath, "MyProject.celbridge");

        var result = await _projectTemplateService.GetConflictingFileNamesAsync(projectFilePath, template);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal("hello_world.py", "readme.md");
    }

    [Test]
    public async Task GetConflictingFileNames_DifferentCase_FollowsTheFileSystemRules()
    {
        // The reported issue used an uppercase README.md against the template's lowercase readme.md.
        var template = _projectTemplateService.GetDefaultTemplate();

        var projectFolderPath = Path.Combine(_tempRootPath, "ExistingFolder");
        Directory.CreateDirectory(projectFolderPath);
        await File.WriteAllTextAsync(Path.Combine(projectFolderPath, "README.md"), "user content");

        var projectFilePath = Path.Combine(projectFolderPath, "MyProject.celbridge");

        var result = await _projectTemplateService.GetConflictingFileNamesAsync(projectFilePath, template);

        result.IsSuccess.Should().BeTrue();

        var isCaseInsensitiveFileSystem = PathComparison.Comparer.Equals("readme.md", "README.md");
        if (isCaseInsensitiveFileSystem)
        {
            // Reported under the name the user gave it, which is the name that survives the replacement.
            result.Value.Should().Equal("README.md");
        }
        else
        {
            result.Value.Should().BeEmpty();
        }
    }

    [Test]
    public async Task GetConflictingFileNames_FolderWithNothingInCommon_ReportsNothing()
    {
        var template = _projectTemplateService.GetDefaultTemplate();

        // A folder that does not exist yet, which is what creating a subfolder for the project gives.
        var newFolderPath = Path.Combine(_tempRootPath, "NewFolder");
        var newFolderResult = await _projectTemplateService.GetConflictingFileNamesAsync(
            Path.Combine(newFolderPath, "MyProject.celbridge"), template);

        newFolderResult.IsSuccess.Should().BeTrue();
        newFolderResult.Value.Should().BeEmpty();

        // An existing folder holding files the template does not write.
        var existingFolderPath = Path.Combine(_tempRootPath, "ExistingFolder");
        Directory.CreateDirectory(existingFolderPath);
        await File.WriteAllTextAsync(Path.Combine(existingFolderPath, "notes.txt"), "user content");

        var existingFolderResult = await _projectTemplateService.GetConflictingFileNamesAsync(
            Path.Combine(existingFolderPath, "MyProject.celbridge"), template);

        existingFolderResult.IsSuccess.Should().BeTrue();
        existingFolderResult.Value.Should().BeEmpty();
    }

    [Test]
    public async Task CreateFromTemplate_NoGitIgnore_WritesTheCelbridgeOne()
    {
        var template = _projectTemplateService.GetDefaultTemplate();

        var projectFolderPath = Path.Combine(_tempRootPath, "MyProject");
        var projectFilePath = Path.Combine(projectFolderPath, "MyProject.celbridge");

        var result = await _projectTemplateService.CreateFromTemplateAsync(projectFilePath, template);

        result.IsSuccess.Should().BeTrue();

        var gitIgnoreContents = await File.ReadAllTextAsync(Path.Combine(projectFolderPath, ".gitignore"));
        gitIgnoreContents.Should().Contain(".celbridge/");
        gitIgnoreContents.Should().Contain("/downloads/");

        // A fresh file is the canonical one, so it carries no merge marker.
        gitIgnoreContents.Should().NotContain("# Added by Celbridge");
    }

    [Test]
    public async Task CreateFromTemplate_ExistingGitIgnore_AppendsOnlyMissingPatterns()
    {
        var template = _projectTemplateService.GetDefaultTemplate();

        var projectFolderPath = Path.Combine(_tempRootPath, "ExistingRepo");
        Directory.CreateDirectory(projectFolderPath);
        var gitIgnorePath = Path.Combine(projectFolderPath, ".gitignore");
        await File.WriteAllTextAsync(gitIgnorePath, "# The user's rules\nnode_modules/\n*.log\n");

        var projectFilePath = Path.Combine(projectFolderPath, "MyProject.celbridge");

        var result = await _projectTemplateService.CreateFromTemplateAsync(projectFilePath, template);

        result.IsSuccess.Should().BeTrue();

        // Creating a project merges rather than replaces. What the merge itself produces is
        // GitIgnoreWriterTests' subject; this only proves creation runs it.
        var gitIgnoreContents = await File.ReadAllTextAsync(gitIgnorePath);
        gitIgnoreContents.Should().Contain("# The user's rules");
        gitIgnoreContents.Should().Contain("*.log");
        gitIgnoreContents.Should().Contain("# Added by Celbridge");
        gitIgnoreContents.Should().Contain(".celbridge/");
    }
}
