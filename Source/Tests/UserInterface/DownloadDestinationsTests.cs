using Celbridge.Localization;
using Celbridge.Resources;
using Celbridge.Tests.FileSystem;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers the names reserved for downloads in the folder the user chose. An empty folder key is the project folder.
/// The download's name is free on disk and among the reserved destinations, and a taken name gets " (N)" before
/// its extension.
/// </summary>
[TestFixture]
public class DownloadDestinationsTests
{
    private static readonly string ProjectFolderPath =
        Path.Combine(Path.GetTempPath(), "celbridge-download-destination-tests", "project");

    private static readonly ResourceKey DownloadFolder = new("downloads");

    private FakeFileSystem _localFileSystem = null!;
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

        var localizerService = Substitute.For<ILocalizerService>();
        localizerService.GetString(Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(callInfo => callInfo.Arg<string>());

        _destinations = new DownloadDestinations(workspaceWrapper, _localFileSystem, localizerService);
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
    public async Task AName_IsReservedInTheFolderGiven()
    {
        _localFileSystem.SeedFolder(Path.Combine(ProjectFolderPath, "assets", "incoming"));

        var reserveResult = await _destinations.ReserveAsync(new ResourceKey("assets/incoming"), "report.pdf");

        reserveResult.IsSuccess.Should().BeTrue(reserveResult.DiagnosticReport);
        reserveResult.Value.Should().Be(new ResourceKey("assets/incoming/report.pdf"));
    }

    [Test]
    public async Task TheEmptyFolderKey_ReservesInTheProjectFolder_AndUniquifiesATakenName()
    {
        _localFileSystem.SeedFile(Path.Combine(ProjectFolderPath, "report.pdf"), "existing");

        var takenResult = await _destinations.ReserveAsync(ResourceKey.Empty, "report.pdf");
        var freeResult = await _destinations.ReserveAsync(ResourceKey.Empty, "notes.txt");

        takenResult.Value.Should().Be(new ResourceKey("report (1).pdf"));
        freeResult.Value.Should().Be(new ResourceKey("notes.txt"));
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

        var reserveResult = await _destinations.ReserveAsync(DownloadFolder, "Report.pdf");

        reserveResult.Value.Should().Be(new ResourceKey("downloads/Report (1).pdf"));
    }

    [Test]
    public async Task AReservedName_IsSkipped_UntilItIsReleased()
    {
        // Neither download has written a byte yet. Only the first download's reservation tells the two apart.
        var firstResult = await _destinations.ReserveAsync(DownloadFolder, "report.pdf");
        var secondResult = await _destinations.ReserveAsync(DownloadFolder, "report.pdf");

        firstResult.Value.Should().Be(new ResourceKey("downloads/report.pdf"));
        secondResult.Value.Should().Be(new ResourceKey("downloads/report (1).pdf"));

        _destinations.Release(firstResult.Value);
        var thirdResult = await _destinations.ReserveAsync(DownloadFolder, "report.pdf");

        thirdResult.Value.Should().Be(new ResourceKey("downloads/report.pdf"));
    }

    [Test]
    public async Task AReservedNameInAnotherCase_IsSkipped()
    {
        // Neither download has landed yet. On a volume that ignores letter case, the two names are the same file.
        var firstResult = await _destinations.ReserveAsync(DownloadFolder, "Report.pdf");
        var secondResult = await _destinations.ReserveAsync(DownloadFolder, "report.pdf");

        firstResult.Value.Should().Be(new ResourceKey("downloads/Report.pdf"));
        secondResult.Value.Should().Be(new ResourceKey("downloads/report (1).pdf"));
    }
}
