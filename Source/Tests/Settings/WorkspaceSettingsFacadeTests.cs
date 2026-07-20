using Celbridge.FileSystem.Services;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Commands;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Covers the Workspace-scoped panel, search, and editor settings: first-open
/// defaults, per-project independence, the typed WorkspaceSettings facade
/// round-trip through the JSON store, and that ResetPanelCommand resets the
/// current project rather than a global setting.
/// </summary>
[TestFixture]
public class WorkspaceSettingsFacadeTests
{
    private string _rootFolderPath = null!;
    private LocalFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _rootFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(WorkspaceSettingsFacadeTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootFolderPath);

        _fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootFolderPath))
        {
            Directory.Delete(_rootFolderPath, recursive: true);
        }
    }

    private sealed record WorkspaceFixture(
        IBindableWorkspaceSettings Settings,
        IWorkspaceWrapper WorkspaceWrapper,
        WorkspaceSettingsService WorkspaceSettingsService);

    // Loads the per-project JSON store in the given folder and wires a settings
    // service and typed facade over it, exactly as the workspace does at runtime.
    private async Task<WorkspaceFixture> LoadWorkspaceAsync(string folderPath)
    {
        var workspaceSettingsService = new WorkspaceSettingsService(_fileSystem);
        workspaceSettingsService.WorkspaceSettingsFolderPath = folderPath;

        var acquireResult = await workspaceSettingsService.AcquireWorkspaceSettingsAsync();
        acquireResult.IsSuccess.Should().BeTrue();

        // Wire the workspace hub so the settings service walks to the live
        // per-project store, matching the path it uses at runtime.
        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.WorkspaceSettings.Returns(workspaceSettingsService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        workspaceWrapper.HasWorkspaceService.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            new FakeSettingsStore(),
            new FakeCredentialStore(),
            workspaceWrapper);

        var facade = new BindableWorkspaceSettings(settingsService);

        // The facade is what consumers reach through WorkspaceService.BindableWorkspaceSettings.
        workspaceService.BindableWorkspaceSettings.Returns(facade);

        return new WorkspaceFixture(facade, workspaceWrapper, workspaceSettingsService);
    }

    [Test]
    public async Task FreshWorkspace_ReturnsDocumentedDefaults()
    {
        var fixture = await LoadWorkspaceAsync(Path.Combine(_rootFolderPath, "projectA"));
        var settings = fixture.Settings;

        settings.PreferredRegionVisibility.Should().Be(LayoutRegion.All);
        settings.PrimaryPanelWidth.Should().Be(WorkspaceConstants.PrimaryPanelWidth);
        settings.SecondaryPanelWidth.Should().Be(WorkspaceConstants.SecondaryPanelWidth);
        settings.ConsolePanelHeight.Should().Be(WorkspaceConstants.ConsolePanelHeight);
        settings.DetailPanelHeight.Should().Be(WorkspaceConstants.DetailPanelHeight);
        settings.IsConsoleMaximized.Should().BeFalse();
        settings.SearchMatchCase.Should().BeFalse();
        settings.SearchWholeWord.Should().BeFalse();
        settings.ReplaceMode.Should().BeFalse();
        settings.PreviousNewFileExtension.Should().Be(".py");
    }

    [Test]
    public async Task FacadeWrite_AfterFlush_SurvivesReload()
    {
        var folderPath = Path.Combine(_rootFolderPath, "projectA");

        var fixture = await LoadWorkspaceAsync(folderPath);
        fixture.Settings.PrimaryPanelWidth = 480f;
        fixture.Settings.SearchMatchCase = true;
        fixture.Settings.PreviousNewFileExtension = ".md";

        // Writes are deferred, so they only reach disk on flush.
        await fixture.WorkspaceSettingsService.WorkspaceSettingsStore!.FlushAsync();
        fixture.WorkspaceSettingsService.UnloadWorkspaceSettings();

        var reloaded = await LoadWorkspaceAsync(folderPath);
        reloaded.Settings.PrimaryPanelWidth.Should().Be(480f);
        reloaded.Settings.SearchMatchCase.Should().BeTrue();
        reloaded.Settings.PreviousNewFileExtension.Should().Be(".md");
    }

    [Test]
    public async Task SeparateWorkspaces_DoNotShareLayout()
    {
        var workspaceA = await LoadWorkspaceAsync(Path.Combine(_rootFolderPath, "projectA"));
        workspaceA.Settings.PrimaryPanelWidth = 512f;
        await workspaceA.WorkspaceSettingsService.WorkspaceSettingsStore!.FlushAsync();

        var workspaceB = await LoadWorkspaceAsync(Path.Combine(_rootFolderPath, "projectB"));
        workspaceB.Settings.PrimaryPanelWidth.Should().Be(WorkspaceConstants.PrimaryPanelWidth);
    }

    [Test]
    public void FacadeWrite_WithNoWorkspaceLoaded_IsIgnoredAndDoesNotThrow()
    {
        // Mirrors the page-load race: a panel SizeChanged event writes through the
        // facade before the workspace store has been acquired.
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        var settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            new FakeSettingsStore(),
            new FakeCredentialStore(),
            workspaceWrapper);

        var facade = new BindableWorkspaceSettings(settingsService);

        Action write = () => facade.ConsolePanelHeight = 999f;

        write.Should().NotThrow();
        facade.ConsolePanelHeight.Should().Be(WorkspaceConstants.ConsolePanelHeight);
    }

    [Test]
    public async Task ResetPanelCommand_ResetsLayoutForCurrentWorkspace()
    {
        var fixture = await LoadWorkspaceAsync(Path.Combine(_rootFolderPath, "projectA"));
        fixture.Settings.PrimaryPanelWidth = 500f;

        var command = new ResetPanelCommand(fixture.WorkspaceWrapper)
        {
            Region = LayoutRegion.Primary,
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        fixture.Settings.PrimaryPanelWidth.Should().Be(WorkspaceConstants.PrimaryPanelWidth);
    }
}
