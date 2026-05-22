using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Workspace-scoped implementation of ISidecarService. Reads and writes
/// .cel sidecar files through IResourceFileSystem so the chokepoint's
/// atomic-write + retry behaviour applies uniformly. Pure utility helpers
/// (block-name validation, indexable-shape validation) delegate to
/// SidecarHelper so the format internals stay in one place.
/// </summary>
public sealed class SidecarService : ISidecarService
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SidecarService(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public bool IsSidecarKey(ResourceKey resource)
    {
        if (resource.IsEmpty)
        {
            return false;
        }
        return resource.Path.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase);
    }

    public Result<ResourceKey> GetSidecarKey(ResourceKey parent)
    {
        if (parent.IsEmpty)
        {
            return Result<ResourceKey>.Fail("Cannot build a sidecar key for an empty resource.");
        }
        if (IsSidecarKey(parent))
        {
            return Result<ResourceKey>.Fail($"Cannot build a sidecar key for sidecar resource '{parent}': pass the parent resource key instead.");
        }
        return Result<ResourceKey>.Ok(new ResourceKey(parent.FullKey + SidecarHelper.Extension));
    }

    public bool IsValidBlockName(string name) => SidecarHelper.IsValidBlockName(name);

    public bool IsIndexableValue(object? value) => SidecarHelper.IsIndexableValue(value);

    public async Task<Result<SidecarReadResult>> ReadAsync(ResourceKey parent)
    {
        var sidecarKeyResult = GetSidecarKey(parent);
        if (sidecarKeyResult.IsFailure)
        {
            return Result<SidecarReadResult>.Fail(sidecarKeyResult);
        }
        var sidecarKey = sidecarKeyResult.Value;

        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;

        var existsResult = await fileSystem.ExistsAsync(sidecarKey);
        if (existsResult.IsFailure
            || !existsResult.Value)
        {
            return Result<SidecarReadResult>.Ok(new SidecarReadResult(SidecarReadOutcome.NoSidecar, null, null));
        }

        var readResult = await fileSystem.ReadAllTextAsync(sidecarKey);
        if (readResult.IsFailure)
        {
            return Result<SidecarReadResult>.Ok(new SidecarReadResult(SidecarReadOutcome.Broken, null, readResult.FirstErrorMessage));
        }

        var parseResult = SidecarHelper.Parse(readResult.Value);
        if (parseResult.IsFailure)
        {
            return Result<SidecarReadResult>.Ok(new SidecarReadResult(SidecarReadOutcome.Broken, null, parseResult.FirstErrorMessage));
        }

        return Result<SidecarReadResult>.Ok(new SidecarReadResult(SidecarReadOutcome.Healthy, parseResult.Value, null));
    }

    public async Task<Result> MutateFrontmatterAsync(
        ResourceKey parent,
        Action<Dictionary<string, object>> mutate,
        bool createIfMissing = true)
    {
        var sidecarKeyResult = GetSidecarKey(parent);
        if (sidecarKeyResult.IsFailure)
        {
            return Result.Fail(sidecarKeyResult);
        }
        var sidecarKey = sidecarKeyResult.Value;

        var readResult = await ReadAsync(parent);
        if (readResult.IsFailure)
        {
            return Result.Fail(readResult);
        }
        var read = readResult.Value;

        Dictionary<string, object> working;
        IReadOnlyList<SidecarBlock> blocks = Array.Empty<SidecarBlock>();

        switch (read.Outcome)
        {
            case SidecarReadOutcome.Healthy:
                working = new Dictionary<string, object>(read.Content!.Frontmatter, StringComparer.Ordinal);
                blocks = read.Content.Blocks;
                break;

            case SidecarReadOutcome.NoSidecar:
                if (!createIfMissing)
                {
                    return Result.Ok();
                }
                working = new Dictionary<string, object>(StringComparer.Ordinal);
                break;

            case SidecarReadOutcome.Broken:
            default:
                return Result.Fail($"Cannot mutate sidecar '{sidecarKey}': {read.FailureMessage ?? "parse failed"}.");
        }

        mutate(working);

        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
        var composed = SidecarHelper.Compose(working, blocks);
        var writeResult = await fileSystem.WriteAllTextAsync(sidecarKey, composed);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write sidecar '{sidecarKey}'.")
                .WithErrors(writeResult);
        }
        return Result.Ok();
    }

    public async Task<Result> MutateBlocksAsync(
        ResourceKey parent,
        Action<List<SidecarBlock>> mutate,
        bool createIfMissing = true)
    {
        var sidecarKeyResult = GetSidecarKey(parent);
        if (sidecarKeyResult.IsFailure)
        {
            return Result.Fail(sidecarKeyResult);
        }
        var sidecarKey = sidecarKeyResult.Value;

        var readResult = await ReadAsync(parent);
        if (readResult.IsFailure)
        {
            return Result.Fail(readResult);
        }
        var read = readResult.Value;

        Dictionary<string, object> frontmatter;
        List<SidecarBlock> working;

        switch (read.Outcome)
        {
            case SidecarReadOutcome.Healthy:
                frontmatter = new Dictionary<string, object>(read.Content!.Frontmatter, StringComparer.Ordinal);
                working = new List<SidecarBlock>(read.Content.Blocks);
                break;

            case SidecarReadOutcome.NoSidecar:
                if (!createIfMissing)
                {
                    return Result.Ok();
                }
                frontmatter = new Dictionary<string, object>(StringComparer.Ordinal);
                working = new List<SidecarBlock>();
                break;

            case SidecarReadOutcome.Broken:
            default:
                return Result.Fail($"Cannot mutate sidecar '{sidecarKey}': {read.FailureMessage ?? "parse failed"}.");
        }

        mutate(working);

        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;
        var composed = SidecarHelper.Compose(frontmatter, working);
        var writeResult = await fileSystem.WriteAllTextAsync(sidecarKey, composed);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write sidecar '{sidecarKey}'.")
                .WithErrors(writeResult);
        }
        return Result.Ok();
    }
}
