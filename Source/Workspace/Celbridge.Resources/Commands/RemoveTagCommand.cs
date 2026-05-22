using Celbridge.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Removes a tag from the parent resource's .cel sidecar tags list. Idempotent.
/// </summary>
public sealed class RemoveTagCommand : CommandBase, IRemoveTagCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string Tag { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RemoveTagCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var tag = Tag;
        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
        return await sidecarService.MutateFrontmatterAsync(
            Resource,
            dict =>
            {
                if (!dict.TryGetValue(SidecarHelper.TagsFieldName, out var value))
                {
                    return;
                }

                var existing = SidecarHelper.ExtractStringList(value);
                if (!existing.Contains(tag, StringComparer.Ordinal))
                {
                    return;
                }

                var updated = existing.Where(t => !string.Equals(t, tag, StringComparison.Ordinal)).ToList();
                if (updated.Count == 0)
                {
                    dict.Remove(SidecarHelper.TagsFieldName);
                }
                else
                {
                    dict[SidecarHelper.TagsFieldName] = updated;
                }
            },
            createIfMissing: false);
    }
}
