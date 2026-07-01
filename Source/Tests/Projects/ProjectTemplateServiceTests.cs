using Celbridge.Projects.Services;
using Celbridge.Python;
using Celbridge.Tests.FileSystem;
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

        var pythonConfigService = Substitute.For<IPythonConfigService>();
        pythonConfigService.DefaultPythonVersion.Returns("3.13");

        // A real app environment so the test exercises actual bundled-asset path resolution (the
        // AppContext.BaseDirectory layout the Skia heads use) and supplies the temp folder. Its reported
        // version is what should flow into the generated project file.
        var appEnvironment = new AppEnvironment();
        _expectedAppVersion = appEnvironment.GetEnvironmentInfo().AppVersion;

        _projectTemplateService = new ProjectTemplateService(
            stringLocalizer,
            pythonConfigService,
            _fileSystem,
            appEnvironment);

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

        // The version placeholders in the template's project file are substituted from the
        // environment and Python config services.
        var projectContents = await File.ReadAllTextAsync(projectFilePath);
        projectContents.Should().NotContain("<application-version>");
        projectContents.Should().NotContain("<python-version>");
        projectContents.Should().Contain(_expectedAppVersion);
        projectContents.Should().Contain("3.13");
    }
}
