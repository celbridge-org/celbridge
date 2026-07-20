using Celbridge.FileSystem.Services;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Locks in the persistence shape used by DocumentsService for editor state:
/// a Dictionary<string, string> keyed by resource key. A schema change here would silently
/// break state restoration after a workspace reload, which is hard to debug at runtime, so
/// we want a fast unit-level signal.
///
/// This is not a full close-then-reopen integration test; that requires WebView lifecycle
/// management which isn't available in the unit-test environment. But it does cover the
/// serialization round-trip, which is where my historical bugs in this area have lived.
/// </summary>
[TestFixture]
public class EditorStatePersistenceTests
{
    private const string DocumentEditorStatesKey = "DocumentEditorStates";

    private IWorkspaceSettingsService _workspaceSettingsService = null!;
    private string _workspaceFolderPath = null!;

    [SetUp]
    public async Task Setup()
    {
        _workspaceFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(EditorStatePersistenceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceFolderPath);

        _workspaceSettingsService = new WorkspaceSettingsService(
            new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
        _workspaceSettingsService.WorkspaceSettingsFolderPath = _workspaceFolderPath;

        var acquireResult = await _workspaceSettingsService.AcquireWorkspaceSettingsAsync();
        acquireResult.IsSuccess.Should().BeTrue();
    }

    [TearDown]
    public void TearDown()
    {
        _workspaceSettingsService.UnloadWorkspaceSettings();
        if (Directory.Exists(_workspaceFolderPath))
        {
            Directory.Delete(_workspaceFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task EditorStateDictionary_RoundTripsThroughSettings()
    {
        var settings = _workspaceSettingsService.PropertyBag!;
        var editorStates = new Dictionary<string, string>
        {
            ["notes/readme.md"] = "{\"scrollPercentage\":0.42,\"viewMode\":\"Split\"}",
            ["src/main.cs"] = "{\"scrollPercentage\":0.0,\"viewMode\":\"Source\"}",
        };

        await settings.SetPropertyAsync(DocumentEditorStatesKey, editorStates);

        var roundTripped = await settings.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);

        roundTripped.Should().NotBeNull();
        roundTripped!.Should().BeEquivalentTo(editorStates);
    }

    [Test]
    public async Task EditorStateDictionary_SurvivesUnloadAndReload()
    {
        var settings = _workspaceSettingsService.PropertyBag!;
        var editorStates = new Dictionary<string, string>
        {
            ["notes/readme.md"] = "{\"scrollPercentage\":0.5}",
        };

        await settings.SetPropertyAsync(DocumentEditorStatesKey, editorStates);

        // Simulate a workspace unload + reload cycle. This is the boundary where past bugs in
        // editor-state persistence have surfaced (state shape changes silently lose data).
        _workspaceSettingsService.UnloadWorkspaceSettings();
        var reloadResult = await _workspaceSettingsService.AcquireWorkspaceSettingsAsync();
        reloadResult.IsSuccess.Should().BeTrue();

        var reloaded = _workspaceSettingsService.PropertyBag!;
        var restored = await reloaded.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);

        restored.Should().NotBeNull();
        restored!.Should().BeEquivalentTo(editorStates);
    }

    [Test]
    public async Task EditorStateDictionary_EmptyDictionaryRoundTripsAsEmpty()
    {
        var settings = _workspaceSettingsService.PropertyBag!;
        var emptyState = new Dictionary<string, string>();

        await settings.SetPropertyAsync(DocumentEditorStatesKey, emptyState);

        var roundTripped = await settings.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);

        roundTripped.Should().NotBeNull();
        roundTripped!.Should().BeEmpty();
    }
}
