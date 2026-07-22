using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Reads the user's preferred editor for a file from the per-file sidecar '_editor' field, set by
/// "Open with...".
/// </summary>
public class DocumentEditorPreferenceStore
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogger<DocumentEditorPreferenceStore> _logger;

    public DocumentEditorPreferenceStore(
        IWorkspaceWrapper workspaceWrapper,
        ILogger<DocumentEditorPreferenceStore> logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _logger = logger;
    }

    /// <summary>
    /// Reads the resource's sidecar (if any) and returns its '_editor' field as
    /// an EditorId. Returns success with Empty when no sidecar exists,
    /// the sidecar has no '_editor' field, or the field value does not parse.
    /// Returns failure only on unexpected sidecar service errors.
    /// </summary>
    public async Task<Result<EditorId>> GetSidecarPreferenceAsync(ResourceKey fileResource)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        if (sidecarService.IsSidecarKey(fileResource))
        {
            // The sidecar file itself does not have a sidecar pairing of its
            // own, so there is nothing to consult.
            return EditorId.Empty;
        }

        var readResult = await sidecarService.ReadAsync(fileResource);
        if (readResult.IsFailure)
        {
            return Result<EditorId>.Fail($"Failed to read sidecar for '{fileResource}'")
                .WithErrors(readResult);
        }

        var sidecar = readResult.Value;
        if (sidecar.Outcome != SidecarReadOutcome.Healthy
            || sidecar.Content is null)
        {
            return EditorId.Empty;
        }

        if (!sidecar.Content.Fields.TryGetValue(SidecarFieldNames.Editor, out var editorValue)
            || editorValue is not string editorIdString
            || string.IsNullOrWhiteSpace(editorIdString))
        {
            return EditorId.Empty;
        }

        if (!EditorId.TryParse(editorIdString, out var editorId))
        {
            _logger.LogDebug($"Sidecar for '{fileResource}' has malformed editor value '{editorIdString}'");
            return EditorId.Empty;
        }

        return editorId;
    }

    /// <summary>
    /// Returns the editor that should open the given file from its sidecar '_editor' field, or Empty
    /// when the file has no per-file editor override.
    /// </summary>
    public async Task<EditorId> GetPreferredEditorAsync(ResourceKey fileResource)
    {
        var sidecarPreferenceResult = await GetSidecarPreferenceAsync(fileResource);
        if (sidecarPreferenceResult.IsSuccess
            && !sidecarPreferenceResult.Value.IsEmpty)
        {
            return sidecarPreferenceResult.Value;
        }

        return EditorId.Empty;
    }
}
