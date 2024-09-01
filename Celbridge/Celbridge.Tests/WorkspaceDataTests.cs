using Celbridge.Workspace;
using Celbridge.Workspace.Services;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class WorkspaceDataTests
{
    private IWorkspaceDataService? _workspaceDataService;

    private string? _workspaceFolderPath;

    [SetUp]
    public void Setup()
    {
        _workspaceFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", $"{nameof(WorkspaceDataTests)}");
        if (Directory.Exists(_workspaceFolderPath))
        {
            Directory.Delete(_workspaceFolderPath, true);
        }

        Directory.CreateDirectory(_workspaceFolderPath);

        _workspaceDataService = new WorkspaceDataService();
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
    public async Task ICanCreateAndLoadWorkspaceDataAsync()
    {
        Guard.IsNotNull(_workspaceDataService);
        Guard.IsNotNullOrEmpty(_workspaceFolderPath);

        var databasePath = Path.Combine(_workspaceFolderPath, "Workspace.db");

        var createResult = await _workspaceDataService.CreateWorkspaceDataAsync(databasePath);
        createResult.IsSuccess.Should().BeTrue();

        var loadResult = _workspaceDataService.LoadWorkspaceData(databasePath);
        loadResult.IsSuccess.Should().BeTrue();

        //
        // WorkspaceData tests
        //

        var workspaceData = _workspaceDataService.LoadedWorkspaceData!;
        workspaceData.Should().NotBeNull();
        var dataVersion = await workspaceData.GetDataVersionAsync();
        dataVersion.Should().Be(1);

        // 
        // Set and get an expanded folders list in the workspace data
        //
        var expandedFolders = new List<string>() { "a", "b", "c" };
        await workspaceData.SetPropertyAsync("ExpandedFolders", expandedFolders);        

        var expandedFoldersProperty = await workspaceData.GetPropertyAsync<List<string>>("ExpandedFolders");
        Guard.IsNotNull(expandedFoldersProperty);
 
        expandedFolders.SequenceEqual(expandedFoldersProperty);

        //
        // Unload the workspace database
        //

        _workspaceDataService.UnloadWorkspaceData();
        _workspaceDataService.LoadedWorkspaceData.Should().BeNull();

        //
        // Delete the workspace database files
        //

        Directory.Delete(_workspaceFolderPath, true);
        File.Exists(databasePath).Should().BeFalse();
    }
}
