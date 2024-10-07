using Celbridge.Core;
using Celbridge.Workspace;
using Celbridge.Workspace.Services;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class WorkspaceSettingsTests
{
    private IWorkspaceSettingsService? _workspaceSettingsService;

    private string? _workspaceFolderPath;

    [SetUp]
    public void Setup()
    {
        _workspaceFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", $"{nameof(WorkspaceSettingsTests)}");
        if (Directory.Exists(_workspaceFolderPath))
        {
            Directory.Delete(_workspaceFolderPath, true);
        }

        Directory.CreateDirectory(_workspaceFolderPath);

        _workspaceSettingsService = new WorkspaceSettingsService();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workspaceFolderPath))
        {
            Directory.Delete(_workspaceFolderPath!, true);
        }
    }

    [Test]
    public async Task ICanCreateAndLoadWorkspaceSettingsAsync()
    {
        Guard.IsNotNull(_workspaceSettingsService);
        Guard.IsNotNullOrEmpty(_workspaceFolderPath);

        var databaseFilePath = Path.Combine(_workspaceFolderPath, FileNameConstants.WorkspaceSettingsFile);

        var createResult = await _workspaceSettingsService.CreateWorkspaceSettingsAsync(databaseFilePath);
        createResult.IsSuccess.Should().BeTrue();

        var loadResult = _workspaceSettingsService.LoadWorkspaceSettings(databaseFilePath);
        loadResult.IsSuccess.Should().BeTrue();

        //
        // Check data version
        //

        var workspaceSettings = _workspaceSettingsService.LoadedWorkspaceSettings!;
        workspaceSettings.Should().NotBeNull();
        var dataVersion = await workspaceSettings.GetDataVersionAsync();
        dataVersion.Should().Be(1);

        // 
        // Set and get an expanded folders list in the workspace settings
        //
        var expandedFolders = new List<string>() { "a", "b", "c" };
        await workspaceSettings.SetPropertyAsync("ExpandedFolders", expandedFolders);        

        var expandedFoldersProperty = await workspaceSettings.GetPropertyAsync<List<string>>("ExpandedFolders");
        Guard.IsNotNull(expandedFoldersProperty);
 
        expandedFolders.SequenceEqual(expandedFoldersProperty);

        //
        // Unload the workspace settings database
        //

        _workspaceSettingsService.UnloadWorkspaceSettings();
        _workspaceSettingsService.LoadedWorkspaceSettings.Should().BeNull();

        //
        // Delete the workspace settings database file
        //

        Directory.Delete(_workspaceFolderPath, true);
        File.Exists(databaseFilePath).Should().BeFalse();
    }
}
