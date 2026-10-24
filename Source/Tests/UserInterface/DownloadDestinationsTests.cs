using Celbridge.Localization;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers where downloads land. A download lands in the folder the project names, or in downloads/ for a
/// project that names an unusable folder or none. The download's name is free on disk and among the reserved
/// destinations.
/// </summary>
[TestFixture]
public class DownloadDestinationsTests
{
    private static readonly string ProjectFolderPath =
        Path.Combine(Path.GetTempPath(), "celbridge-download-destination-tests", "project");

    private FakeFileSystem _localFileSystem = null!;
    private IProject _project = null!;
    private DownloadDestinations _destinations = null!;

    [SetUp]
    public void Setup()
    {
        _localFileSystem = new FakeFileSystem();
        _localFileSystem.SeedFolder(Path.Combine(ProjectFolderPath, "downloads"));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(ProjectFolderPath));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.HasWorkspaceService.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // The project names no downloads folder until a test names one.
        _project = Substitute.For<IProject>();
        _project.Config.Returns(new ProjectConfig());

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(_project);

        var localizerService = Substitute.For<ILocalizerService>();
        localizerService.GetString(Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(callInfo => callInfo.Arg<string>());

        _destinations = new DownloadDestinations(workspaceWrapper, projectService, _localFileSystem, localizerService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(ProjectFolderPath))
        {
            Directory.Delete(ProjectFolderPath, recursive: true);
        }
    }

    [Test]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive file system, Report.pdf and report.pdf are different files.")]
    public async Task ANameOnDiskInAnotherCase_IsUniquified()
    {
        // The resolver reads the stored names from the real disk, so the test writes the file there too. The
        // in-memory file system compares names exactly. The test therefore gives the in-memory file system the file
        // under both names, matching a volume that ignores case.
        var existingPath = Path.Combine(ProjectFolderPath, "downloads", "report.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(existingPath)!);
        File.WriteAllText(existingPath, "existing");
        _localFileSystem.SeedFile(existingPath, "existing");
        _localFileSystem.SeedFile(Path.Combine(ProjectFolderPath, "downloads", "Report.pdf"), "existing");

        var reserveResult = await _destinations.ReserveAsync("Report.pdf");

        reserveResult.Value.Should().Be(new ResourceKey("downloads/Report (1).pdf"));
    }

    [Test]
    public async Task AFolderTheProjectNames_IsWhereDownloadsLand()
    {
        _localFileSystem.SeedFolder(Path.Combine(ProjectFolderPath, "assets", "incoming"));
        NameDownloadsFolder("assets/incoming");

        var reserveResult = await _destinations.ReserveAsync("report.pdf");

        reserveResult.Value.Should().Be(new ResourceKey("assets/incoming/report.pdf"));
    }

    /// <summary>
    /// A load drops a path that is not a folder path, and a file can take the folder's place after the
    /// project named it. Either way the download still lands, in the default folder.
    /// </summary>
    [TestCase("notes.txt", Description = "a file rather than a folder")]
    [TestCase("../outside", Description = "a path that leaves the project")]
    public async Task AFolderTheProjectCannotUse_LeavesDownloadsInTheDefaultFolder(string downloadsFolder)
    {
        _localFileSystem.SeedFile(Path.Combine(ProjectFolderPath, "notes.txt"), "notes");
        NameDownloadsFolder(downloadsFolder);

        var reserveResult = await _destinations.ReserveAsync("report.pdf");

        reserveResult.Value.Should().Be(new ResourceKey("downloads/report.pdf"));
    }

    [Test]
    public async Task AReservedName_IsSkipped_UntilItIsReleased()
    {
        // Neither download has written a byte yet. Only the first download's reservation tells the two apart.
        var firstResult = await _destinations.ReserveAsync("report.pdf");
        var secondResult = await _destinations.ReserveAsync("report.pdf");

        firstResult.Value.Should().Be(new ResourceKey("downloads/report.pdf"));
        secondResult.Value.Should().Be(new ResourceKey("downloads/report (1).pdf"));

        _destinations.Release(firstResult.Value);
        var thirdResult = await _destinations.ReserveAsync("report.pdf");

        thirdResult.Value.Should().Be(new ResourceKey("downloads/report.pdf"));
    }

    [Test]
    public void TheFolderPath_IsTheFolderTheProjectNames()
    {
        // The web view saves downloads to this folder without a prompt. A path anywhere else can only come
        // from a Save As dialog.
        var folderResult = _destinations.GetFolderPath();

        folderResult.IsSuccess.Should().BeTrue(folderResult.DiagnosticReport);
        folderResult.Value.Should().Be(Path.Combine(ProjectFolderPath, "downloads"));
    }

    private void NameDownloadsFolder(string downloadsFolder)
    {
        var config = new ProjectConfig
        {
            Resources = new ResourcesSection
            {
                DownloadsFolder = downloadsFolder
            }
        };

        _project.Config.Returns(config);
    }
}
