using Celbridge.BaseLibrary.Workspace;
using Celbridge.Workspace.Services;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class WorkspaceDataTests
{
    private IWorkspaceDataService? _workspaceDataService;

    private string? _workspaceFolder;

    [SetUp]
    public void Setup()
    {
        _workspaceFolder = Path.Combine(Path.GetTempPath(), "Celbridge", $"{nameof(WorkspaceDataTests)}");
        if (Directory.Exists(_workspaceFolder))
        {
            Directory.Delete(_workspaceFolder, true);
        }

        Directory.CreateDirectory(_workspaceFolder);

        _workspaceDataService = new WorkspaceDataService();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_workspaceFolder))
        {
            Directory.Delete(_workspaceFolder!, true);
        }
    }

    [Test]
    public async Task ICanCreateAndLoadWorkspaceDataAsync()
    {
        Guard.IsNotNull(_workspaceDataService);
        Guard.IsNotNullOrEmpty(_workspaceFolder);

        var databasePath = Path.Combine(_workspaceFolder, "Workspace.db");

        var createResult = await _workspaceDataService.CreateWorkspaceDataAsync(databasePath);
        createResult.IsSuccess.Should().BeTrue();

        var loadResult = _workspaceDataService.LoadWorkspaceData(databasePath);
        loadResult.IsSuccess.Should().BeTrue();

        //
        // WorkspaceData tests
        //

        var workspaceData = _workspaceDataService.LoadedWorkspaceData!;
        workspaceData.Should().NotBeNull();
        var versionResult = await workspaceData.GetDataVersionAsync();
        versionResult.IsSuccess.Should().BeTrue();
        versionResult.Value.Should().Be(1);

        // 
        // Set and get an expanded folders list in the workspace data
        //
        var expandedFolders = new List<string>() { "a", "b", "c" };
        var setFoldersResult = await workspaceData.SetExpandedFoldersAsync(expandedFolders);
        setFoldersResult.IsSuccess.Should().BeTrue();

        var getFoldersResult = await workspaceData.GetExpandedFoldersAsync();
        getFoldersResult.IsSuccess.Should().BeTrue();

        expandedFolders.SequenceEqual(getFoldersResult.Value);

        //
        // Unload the workspace database
        //

        _workspaceDataService.UnloadWorkspaceData();
        _workspaceDataService.LoadedWorkspaceData.Should().BeNull();

        //
        // Delete the workspace database files
        //

        Directory.Delete(_workspaceFolder, true);
        File.Exists(databasePath).Should().BeFalse();
    }
}
