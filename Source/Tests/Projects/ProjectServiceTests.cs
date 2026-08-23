using Celbridge.FileSystem;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Covers the recent projects list: the order it comes back in, the entries it drops, and the fact that
/// excluding the open project is matched by path rather than by taking the head off the list.
/// </summary>
[TestFixture]
public class ProjectServiceTests
{
    private static readonly string NewestProject = MakeProjectFilePath("Newest");
    private static readonly string DeletedProject = MakeProjectFilePath("Deleted");
    private static readonly string OldestProject = MakeProjectFilePath("Oldest");

    private ISettingsService _settingsService = null!;
    private ILocalFileSystem _fileSystem = null!;
    private IMessengerService _messengerService = null!;
    private ProjectService _projectService = null!;

    [SetUp]
    public void Setup()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _fileSystem = Substitute.For<ILocalFileSystem>();

        StubProjectExists(NewestProject);
        StubProjectExists(OldestProject);
        StubProjectMissing(DeletedProject);

        var projectFactory = new ProjectFactory(
            new NullLogger<ProjectFactory>(),
            _fileSystem);

        _messengerService = new MessengerService();

        _projectService = new ProjectService(
            new NullLogger<ProjectService>(),
            _settingsService,
            _messengerService,
            projectFactory,
            Substitute.For<IProjectTemplateService>(),
            _fileSystem);
    }

    [Test]
    public void RenamingTheProjectFile_UpdatesTheLoadedProjectIdentity()
    {
        var project = LoadProjectNamed(NewestProject);

        _messengerService.Send(new ResourceKeyChangedMessage(
            new ResourceKey("Newest.celbridge"),
            new ResourceKey("Renamed.celbridge")));

        var current = _projectService.CurrentProject;
        current.Should().NotBeNull();
        current!.ProjectName.Should().Be("Renamed");
        current.ProjectFilePath.Should().Be(Path.Combine(project.ProjectFolderPath, "Renamed.celbridge"));
        current.ProjectFolderPath.Should().Be(project.ProjectFolderPath);
    }

    [Test]
    public void RenamingTheProjectFile_RewritesItsRecentProjectsEntryInPlace()
    {
        var project = LoadProjectNamed(NewestProject);

        var stored = new List<string> { OldestProject, project.ProjectFilePath, DeletedProject };
        _settingsService.Get(SettingCatalog.Project.RecentProjects).Returns(stored);

        _messengerService.Send(new ResourceKeyChangedMessage(
            new ResourceKey("Newest.celbridge"),
            new ResourceKey("Renamed.celbridge")));

        var renamedPath = Path.Combine(project.ProjectFolderPath, "Renamed.celbridge");
        _settingsService.Received().Set(
            SettingCatalog.Project.RecentProjects,
            Arg.Is<List<string>>(list => list.Count == 3 && list[1] == renamedPath));
    }

    [Test]
    public void MovingTheProjectFileIntoAFolder_LeavesTheLoadedProjectAlone()
    {
        // A move changes the project folder, which the loaded workspace has already resolved every path
        // against, so it is reported rather than reconciled.
        var project = LoadProjectNamed(NewestProject);

        _messengerService.Send(new ResourceKeyChangedMessage(
            new ResourceKey("Newest.celbridge"),
            new ResourceKey("sub/Newest.celbridge")));

        _projectService.CurrentProject!.ProjectFilePath.Should().Be(project.ProjectFilePath);
    }

    [Test]
    public void GetRecentProjects_ReturnsTheStoredOrder()
    {
        // The load path inserts at the front, so the stored order is most recently opened first.
        StubStoredProjects(NewestProject, OldestProject);

        var recentProjects = _projectService.GetRecentProjects(excludeCurrentProject: false);

        recentProjects.Select(recentProject => recentProject.ProjectFilePath)
            .Should().Equal(NewestProject, OldestProject);
    }

    [Test]
    public void GetRecentProjects_DropsProjectsThatAreNoLongerOnDisk()
    {
        StubStoredProjects(NewestProject, DeletedProject, OldestProject);

        var recentProjects = _projectService.GetRecentProjects(excludeCurrentProject: false);

        recentProjects.Select(recentProject => recentProject.ProjectFilePath)
            .Should().Equal(NewestProject, OldestProject);
    }

    [Test]
    public void GetRecentProjects_ExcludingTheCurrentProject_KeepsTheMostRecentEntryWhenNoProjectIsOpen()
    {
        StubStoredProjects(NewestProject, OldestProject);

        var recentProjects = _projectService.GetRecentProjects(excludeCurrentProject: true);

        // Home asks for the list right after a close, before the project is cleared elsewhere. Dropping the
        // first entry outright would hide the project the user just closed.
        recentProjects.Select(recentProject => recentProject.ProjectFilePath)
            .Should().Equal(NewestProject, OldestProject);
    }

    // RecentProject splits the path into a folder and a name and requires both, so the separators have to
    // be the running platform's. These tests run on Linux in CI.
    private static string MakeProjectFilePath(string projectName)
    {
        return Path.Combine(Path.GetTempPath(), projectName, $"{projectName}.celbridge");
    }

    [Test]
    public void IsProjectFile_MatchesTheProjectFileAndNothingElse()
    {
        // The key's canonical form carries a "project:" root prefix, so a comparison against the bare
        // file name has to use Path rather than ToString.
        var project = LoadProjectNamed(NewestProject);

        project.IsProjectFile(new ResourceKey("Newest.celbridge")).Should().BeTrue();
        project.IsProjectFile(new ResourceKey("Other.celbridge")).Should().BeFalse();
        project.IsProjectFile(new ResourceKey("sub/Newest.celbridge")).Should().BeFalse();
        project.IsProjectFile(new ResourceKey("temp:Newest.celbridge")).Should().BeFalse();
    }

    // Loads a project through the real service so CurrentProject is populated the way the rename handler
    // finds it. The stubbed file system makes the config parse fail, which these tests do not care about.
    private IProject LoadProjectNamed(string projectFilePath)
    {
        // An unstubbed settings read returns null, which the recent-projects update would throw on.
        StubStoredProjects();

        var migrationResult = MigrationResult.Success();
        var loadResult = _projectService.LoadProjectAsync(projectFilePath, migrationResult).GetAwaiter().GetResult();
        loadResult.IsSuccess.Should().BeTrue();

        return loadResult.Value;
    }

    private void StubStoredProjects(params string[] projectFilePaths)
    {
        var storedProjects = new List<string>(projectFilePaths);
        _settingsService.Get(SettingCatalog.Project.RecentProjects).Returns(storedProjects);
    }

    private void StubProjectExists(string projectFilePath)
    {
        var itemInfo = new StorageItemInfo(StorageItemKind.File, 0, DateTime.UnixEpoch, default);
        _fileSystem.GetInfoAsync(projectFilePath).Returns(Result<StorageItemInfo>.Ok(itemInfo));
    }

    private void StubProjectMissing(string projectFilePath)
    {
        _fileSystem.GetInfoAsync(projectFilePath)
            .Returns(Result<StorageItemInfo>.Fail($"No item at '{projectFilePath}'"));
    }
}
