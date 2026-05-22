using Celbridge.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Appends a tag to the parent resource's .cel sidecar tags list, creating
/// the sidecar if missing. Idempotent.
/// </summary>
public sealed class AddTagCommand : CommandBase, IAddTagCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey Resource { get; set; }
    public string Tag { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public AddTagCommand(IWorkspaceWrapper workspaceWrapper)
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
                var existing = dict.TryGetValue(SidecarHelper.TagsFieldName, out var value)
                    ? SidecarHelper.ExtractStringList(value)
                    : Array.Empty<string>();

                if (existing.Contains(tag, StringComparer.Ordinal))
                {
                    return;
                }

                var updated = new List<string>(existing.Count + 1);
                updated.AddRange(existing);
                updated.Add(tag);
                dict[SidecarHelper.TagsFieldName] = updated;
            });
    }
}
