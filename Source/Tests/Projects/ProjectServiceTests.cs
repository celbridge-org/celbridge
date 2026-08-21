using Celbridge.FileSystem;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Settings;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Projects;

/// <summary>
/// Covers the recent projects list: the order it comes back in, the entries it drops, and the fact that
/// excluding the open project is matched by path rather than by taking the head off the list.
/// </summary>
[TestFixture]
public class ProjectServiceTests
{
    private const string NewestProject = @"C:\Projects\Newest\Newest.celbridge";
    private const string DeletedProject = @"C:\Projects\Deleted\Deleted.celbridge";
    private const string OldestProject = @"C:\Projects\Oldest\Oldest.celbridge";

    private ISettingsService _settingsService = null!;
    private ILocalFileSystem _fileSystem = null!;
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

        _projectService = new ProjectService(
            new NullLogger<ProjectService>(),
            _settingsService,
            projectFactory,
            Substitute.For<IProjectTemplateService>(),
            _fileSystem);
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
