using Celbridge.Projects;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Locks in the persistence shape used by DocumentsService for editor state:
/// a Dictionary&lt;string, string&gt; keyed by resource key. A schema change here would silently
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
    private string _databaseFilePath = null!;

    [SetUp]
    public async Task Setup()
    {
        _workspaceFolderPath = Path.Combine(Path.GetTempPath(), "Celbridge", $"{nameof(EditorStatePersistenceTests)}", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspaceFolderPath);

        _databaseFilePath = Path.Combine(_workspaceFolderPath, ProjectConstants.WorkspaceSettingsFile);

        _workspaceSettingsService = new WorkspaceSettingsService();
        var createResult = await _workspaceSettingsService.CreateWorkspaceSettingsAsync(_databaseFilePath);
        createResult.IsSuccess.Should().BeTrue();

        // Create only writes the database file; we still need to load it so WorkspaceSettings
        // is populated for the test.
        var loadResult = _workspaceSettingsService.LoadWorkspaceSettings(_databaseFilePath);
        loadResult.IsSuccess.Should().BeTrue();
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
        var settings = _workspaceSettingsService.WorkspaceSettings!;
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
        var settings = _workspaceSettingsService.WorkspaceSettings!;
        var editorStates = new Dictionary<string, string>
        {
            ["notes/readme.md"] = "{\"scrollPercentage\":0.5}",
        };

        await settings.SetPropertyAsync(DocumentEditorStatesKey, editorStates);

        // Simulate a workspace unload + reload cycle. This is the boundary where past bugs in
        // editor-state persistence have surfaced (state shape changes silently lose data).
        _workspaceSettingsService.UnloadWorkspaceSettings();
        var reloadResult = _workspaceSettingsService.LoadWorkspaceSettings(_databaseFilePath);
        reloadResult.IsSuccess.Should().BeTrue();

        var reloaded = _workspaceSettingsService.WorkspaceSettings!;
        var restored = await reloaded.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);

        restored.Should().NotBeNull();
        restored!.Should().BeEquivalentTo(editorStates);
    }

    [Test]
    public async Task EditorStateDictionary_EmptyDictionaryRoundTripsAsEmpty()
    {
        var settings = _workspaceSettingsService.WorkspaceSettings!;
        var emptyState = new Dictionary<string, string>();

        await settings.SetPropertyAsync(DocumentEditorStatesKey, emptyState);

        var roundTripped = await settings.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);

        roundTripped.Should().NotBeNull();
        roundTripped!.Should().BeEmpty();
    }

    [Test]
    public async Task EditorPreference_RoundTripsThroughSettings()
    {
        // Locks in the storage shape used by IDocumentsService.GetEditorPreferenceAsync /
        // SetEditorPreferenceAsync. The key format is a private detail of the service but the
        // value contract (a string editor id) needs to round-trip cleanly.
        var settings = _workspaceSettingsService.WorkspaceSettings!;
        var preferenceKey = "DocumentEditorPreference:.md";
        var editorId = "celbridge.markdown-editor";

        await settings.SetPropertyAsync(preferenceKey, editorId);

        var roundTripped = await settings.GetPropertyAsync<string>(preferenceKey);

        roundTripped.Should().Be(editorId);
    }
}
