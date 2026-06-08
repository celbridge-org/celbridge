using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

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

    public bool IsSidecarFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }
        return fileName.EndsWith(SidecarHelper.Extension, StringComparison.OrdinalIgnoreCase);
    }

    public Result<ResourceKey> GetSidecarKey(ResourceKey parent)
    {
        if (parent.IsEmpty)
        {
            return Result.Fail("Cannot build a sidecar key for an empty resource.");
        }
        if (parent.Root != ResourceKey.DefaultRoot)
        {
            return Result.Fail($"Sidecars are only supported on the project root; resource '{parent}' is on root '{parent.Root}'.");
        }
        if (IsSidecarKey(parent))
        {
            return Result.Fail($"Cannot build a sidecar key for sidecar resource '{parent}': pass the parent resource key instead.");
        }

        return new ResourceKey(parent.FullKey + SidecarHelper.Extension);
    }

    public bool IsValidBlockName(string name) => SidecarHelper.IsValidBlockName(name);

    public bool IsIndexableValue(object? value) => SidecarHelper.IsIndexableValue(value);

    public async Task<Result<SidecarReadResult>> ReadAsync(ResourceKey resource)
    {
        var resolveResult = ResolveSidecarKey(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }
        var sidecarKey = resolveResult.Value;

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var infoResult = await resourceFileSystem.GetInfoAsync(sidecarKey);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return new SidecarReadResult(SidecarReadOutcome.NoSidecar, null, null);
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(sidecarKey);
        if (readResult.IsFailure)
        {
            return new SidecarReadResult(SidecarReadOutcome.Broken, null, readResult.FirstErrorMessage);
        }

        var parseResult = SidecarHelper.Parse(readResult.Value);
        if (parseResult.IsFailure)
        {
            return new SidecarReadResult(SidecarReadOutcome.Broken, null, parseResult.FirstErrorMessage);
        }

        return new SidecarReadResult(SidecarReadOutcome.Healthy, parseResult.Value, null);
    }

    public async Task<Result> SetFieldAsync(ResourceKey resource, string field, object value)
    {
        if (string.IsNullOrEmpty(field))
        {
            return Result.Fail("Field name is empty.");
        }
        if (value is null)
        {
            return Result.Fail("Value is null. Use RemoveFieldAsync to clear a field.");
        }
        if (!SidecarHelper.IsIndexableValue(value))
        {
            return Result.Fail($"Field '{field}' value is not indexable. Only scalar (string/number/bool/datetime) and list-of-scalar values are supported.");
        }

        return await MutateFrontmatterAsync(
            resource,
            dictionary => dictionary[field] = value);
    }

    public async Task<Result> RemoveFieldAsync(ResourceKey resource, string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return Result.Fail("Field name is empty.");
        }

        return await MutateFrontmatterAsync(
            resource,
            dictionary => dictionary.Remove(field),
            createIfMissing: false);
    }

    public async Task<Result> AddTagAsync(ResourceKey resource, string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Result.Fail("Tag is empty.");
        }

        return await MutateFrontmatterAsync(
            resource,
            dictionary =>
            {
                var existing = dictionary.TryGetValue(SidecarHelper.TagsFieldName, out var value)
                    ? SidecarHelper.ExtractStringList(value)
                    : Array.Empty<string>();

                if (existing.Contains(tag, StringComparer.Ordinal))
                {
                    return;
                }

                var updated = new List<string>(existing.Count + 1);
                updated.AddRange(existing);
                updated.Add(tag);
                dictionary[SidecarHelper.TagsFieldName] = updated;
            });
    }

    public async Task<Result> RemoveTagAsync(ResourceKey resource, string tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Result.Fail("Tag is empty.");
        }

        return await MutateFrontmatterAsync(
            resource,
            dictionary =>
            {
                if (!dictionary.TryGetValue(SidecarHelper.TagsFieldName, out var value))
                {
                    return;
                }

                var existing = SidecarHelper.ExtractStringList(value);
                if (!existing.Contains(tag, StringComparer.Ordinal))
                {
                    return;
                }

                var updated = existing.Where(other => !string.Equals(other, tag, StringComparison.Ordinal)).ToList();
                if (updated.Count == 0)
                {
                    dictionary.Remove(SidecarHelper.TagsFieldName);
                }
                else
                {
                    dictionary[SidecarHelper.TagsFieldName] = updated;
                }
            },
            createIfMissing: false);
    }

    public async Task<Result> WriteBlockAsync(ResourceKey resource, string blockId, string content)
    {
        if (!SidecarHelper.IsValidBlockName(blockId))
        {
            return Result.Fail($"Block id '{blockId}' does not match the block-naming rules (lowercase letters, digits, hyphens, dotted segments).");
        }
        if (content is null)
        {
            return Result.Fail("Block content is null.");
        }
        if (SidecarHelper.BlockContentContainsFenceLine(content))
        {
            return Result.Fail($"Block '{blockId}' content contains a line matching the fence regex (e.g. '+++ \"name\"'); this would corrupt the sidecar on round-trip.");
        }

        return await MutateBlocksAsync(
            resource,
            blocks =>
            {
                var index = -1;
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (string.Equals(blocks[i].Name, blockId, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }

                var updated = new SidecarBlock(blockId, content);
                if (index >= 0)
                {
                    blocks[index] = updated;
                }
                else
                {
                    blocks.Add(updated);
                }
            });
    }

    public async Task<Result> RemoveBlockAsync(ResourceKey resource, string blockId)
    {
        if (string.IsNullOrEmpty(blockId))
        {
            return Result.Fail("Block id is empty.");
        }

        return await MutateBlocksAsync(
            resource,
            blocks =>
            {
                for (int i = blocks.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(blocks[i].Name, blockId, StringComparison.Ordinal))
                    {
                        blocks.RemoveAt(i);
                    }
                }
            },
            createIfMissing: false);
    }

    private Task<Result> MutateFrontmatterAsync(
        ResourceKey resource,
        Action<Dictionary<string, object>> mutate,
        bool createIfMissing = true)
    {
        return ApplyMutationAsync(
            resource,
            context => mutate(context.Frontmatter),
            createIfMissing);
    }

    private Task<Result> MutateBlocksAsync(
        ResourceKey resource,
        Action<List<SidecarBlock>> mutate,
        bool createIfMissing = true)
    {
        return ApplyMutationAsync(
            resource,
            context => mutate(context.Blocks),
            createIfMissing);
    }

    // The shared read-modify-write engine behind every typed mutator. Loads the
    // current sidecar state into mutable working copies, runs the supplied
    // mutation, then writes the composed result back through the gateway.
    // The pre-mutation compose is captured up front so the post-mutation compose
    // can be compared against it; when they match the write is skipped, so a
    // no-op mutate (AddTagAsync with an already-present tag, SetFieldAsync to
    // the current value) does not trigger a watcher event or downstream
    // resource refresh. A missing storage file is either created
    // (createIfMissing=true) or quietly skipped (createIfMissing=false); a
    // Broken sidecar fails rather than being overwritten with fresh content.
    private async Task<Result> ApplyMutationAsync(
        ResourceKey resource,
        Action<MutationContext> applyMutation,
        bool createIfMissing)
    {
        var resolveResult = ResolveSidecarKey(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }
        var sidecarKey = resolveResult.Value;

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var readResult = await ReadAsync(resource);
        if (readResult.IsFailure)
        {
            return Result.Fail(readResult);
        }
        var read = readResult.Value;

        Dictionary<string, object> frontmatter;
        List<SidecarBlock> blocks;

        switch (read.Outcome)
        {
            case SidecarReadOutcome.Healthy:
                frontmatter = new Dictionary<string, object>(read.Content!.Frontmatter, StringComparer.Ordinal);
                blocks = new List<SidecarBlock>(read.Content.Blocks);
                break;

            case SidecarReadOutcome.NoSidecar:
                if (!createIfMissing)
                {
                    return Result.Ok();
                }
                // Creating a fresh sidecar requires the parent file to exist
                // on disk. The .cel extension is reserved for sidecars, so the
                // would-be parent is always the sidecar key with the trailing
                // .cel stripped. Refusing the write keeps orphan .cel files
                // from materialising when a caller passes a key that does not
                // match an on-disk file.
                var parentKey = StripSidecarSuffix(sidecarKey);
                var parentInfoResult = await resourceFileSystem.GetInfoAsync(parentKey);
                if (parentInfoResult.IsFailure
                    || parentInfoResult.Value.Kind != StorageItemKind.File)
                {
                    return Result.Fail(
                        $"Cannot create sidecar '{sidecarKey}': the parent file '{parentKey}' does not exist on disk. "
                        + "The .cel extension is reserved for project metadata sidecars; pass the parent resource key.");
                }
                frontmatter = new Dictionary<string, object>(StringComparer.Ordinal);
                blocks = new List<SidecarBlock>();
                break;

            case SidecarReadOutcome.Broken:
            default:
                return Result.Fail($"Cannot mutate sidecar '{sidecarKey}': {read.FailureMessage ?? "parse failed"}.");
        }

        var canonicalBefore = SidecarHelper.Compose(frontmatter, blocks);
        var context = new MutationContext(frontmatter, blocks);
        applyMutation(context);
        var canonicalAfter = SidecarHelper.Compose(frontmatter, blocks);

        if (string.Equals(canonicalAfter, canonicalBefore, StringComparison.Ordinal))
        {
            return Result.Ok();
        }

        var writeResult = await resourceFileSystem.WriteAllTextAsync(sidecarKey, canonicalAfter);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write sidecar '{sidecarKey}'.")
                .WithErrors(writeResult);
        }
        return Result.Ok();
    }

    // The mutable working state passed to ApplyMutationAsync's callback. Direct
    // mutation of either collection is the way edits land.
    private sealed record MutationContext(
        Dictionary<string, object> Frontmatter,
        List<SidecarBlock> Blocks);

    // Resolves the resource key whose file holds the frontmatter+blocks for the
    // given resource. For a regular file this is the sibling .cel key produced
    // by GetSidecarKey. A .cel key passed directly returns unchanged — the data
    // tools reject such keys at the agent layer, but internal callers may still
    // operate on a sidecar's own key. Non-project roots are refused outright:
    // sidecar metadata is a project-scoped system and the tracking pass only
    // scans the project tree, so cross-root sidecars would be silently
    // invisible to validation.
    private Result<ResourceKey> ResolveSidecarKey(ResourceKey resource)
    {
        if (resource.IsEmpty)
        {
            return Result.Fail("Cannot resolve sidecar key for an empty resource.");
        }
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result.Fail($"Sidecars are only supported on the project root; resource '{resource}' is on root '{resource.Root}'.");
        }
        if (IsSidecarKey(resource))
        {
            return resource;
        }

        return GetSidecarKey(resource);
    }

    // Strips the trailing .cel from a sidecar key to derive its parent key.
    // Caller must guarantee the input is a .cel key (e.g. via IsSidecarKey or
    // by construction). Used by the orphan-prevention check inside
    // ApplyMutationAsync.
    private static ResourceKey StripSidecarSuffix(ResourceKey sidecarKey)
    {
        var fullKey = sidecarKey.FullKey;
        var trimmed = fullKey.Substring(0, fullKey.Length - SidecarHelper.Extension.Length);
        return new ResourceKey(trimmed);
    }
}
