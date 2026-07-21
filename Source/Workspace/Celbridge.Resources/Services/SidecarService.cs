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
        return resource.Path.EndsWith(SidecarFile.Extension, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsSidecarFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }
        return fileName.EndsWith(SidecarFile.Extension, StringComparison.OrdinalIgnoreCase);
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

        return new ResourceKey(parent.FullKey + SidecarFile.Extension);
    }

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

    public async Task<Result<SidecarWriteOutcome>> SetFieldsAsync(ResourceKey resource, IReadOnlyDictionary<string, object> fields)
    {
        if (fields is null)
        {
            return Result<SidecarWriteOutcome>.Fail("Fields dictionary is null.");
        }
        if (fields.Count == 0)
        {
            return SidecarWriteOutcome.NoChange;
        }

        foreach (var (name, value) in fields)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Result<SidecarWriteOutcome>.Fail("Field name is empty.");
            }
            if (value is null)
            {
                return Result<SidecarWriteOutcome>.Fail($"Field '{name}' value is null. Use RemoveFieldsAsync to clear fields.");
            }
            if (!SidecarHelper.IsIndexableValue(value))
            {
                return Result<SidecarWriteOutcome>.Fail($"Field '{name}' value is not indexable. Only scalar (string/number/bool/datetime) and list-of-scalar values are supported.");
            }
        }

        return await MutateFieldsAsync(
            resource,
            dictionary =>
            {
                foreach (var (name, value) in fields)
                {
                    dictionary[name] = value;
                }
            });
    }

    public async Task<Result<SidecarWriteOutcome>> RemoveFieldsAsync(ResourceKey resource, IReadOnlyList<string> names)
    {
        if (names is null)
        {
            return Result<SidecarWriteOutcome>.Fail("Field names list is null.");
        }
        if (names.Count == 0)
        {
            return SidecarWriteOutcome.NoChange;
        }

        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Result<SidecarWriteOutcome>.Fail("Field name is empty.");
            }
        }

        return await MutateFieldsAsync(
            resource,
            dictionary =>
            {
                foreach (var name in names)
                {
                    dictionary.Remove(name);
                }
            },
            createIfMissing: false);
    }

    public async Task<Result<SidecarWriteOutcome>> AddTagsAsync(ResourceKey resource, IReadOnlyList<string> tags)
    {
        if (tags is null)
        {
            return Result<SidecarWriteOutcome>.Fail("Tags list is null.");
        }
        if (tags.Count == 0)
        {
            return SidecarWriteOutcome.NoChange;
        }

        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return Result<SidecarWriteOutcome>.Fail("Tag is empty.");
            }
        }

        return await MutateFieldsAsync(
            resource,
            dictionary =>
            {
                var existing = dictionary.TryGetValue(SidecarFieldNames.Tags, out var value)
                    ? SidecarHelper.ExtractStringList(value)
                    : Array.Empty<string>();

                var updated = new List<string>(existing);
                var present = new HashSet<string>(existing, StringComparer.Ordinal);
                foreach (var tag in tags)
                {
                    if (present.Add(tag))
                    {
                        updated.Add(tag);
                    }
                }

                if (updated.Count == existing.Count)
                {
                    return;
                }

                dictionary[SidecarFieldNames.Tags] = updated;
            });
    }

    public async Task<Result<SidecarWriteOutcome>> RemoveTagsAsync(ResourceKey resource, IReadOnlyList<string> tags)
    {
        if (tags is null)
        {
            return Result<SidecarWriteOutcome>.Fail("Tags list is null.");
        }
        if (tags.Count == 0)
        {
            return SidecarWriteOutcome.NoChange;
        }

        foreach (var tag in tags)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return Result<SidecarWriteOutcome>.Fail("Tag is empty.");
            }
        }

        return await MutateFieldsAsync(
            resource,
            dictionary =>
            {
                if (!dictionary.TryGetValue(SidecarFieldNames.Tags, out var value))
                {
                    return;
                }

                var existing = SidecarHelper.ExtractStringList(value);
                var removalSet = new HashSet<string>(tags, StringComparer.Ordinal);
                var updated = existing.Where(other => !removalSet.Contains(other)).ToList();

                if (updated.Count == existing.Count)
                {
                    return;
                }

                if (updated.Count == 0)
                {
                    dictionary.Remove(SidecarFieldNames.Tags);
                }
                else
                {
                    dictionary[SidecarFieldNames.Tags] = updated;
                }
            },
            createIfMissing: false);
    }

    // The shared read-modify-write engine behind every typed mutator. Loads the
    // current sidecar state into a mutable working copy, runs the supplied
    // mutation, then writes the composed result back through the gateway.
    // The pre-mutation compose is captured up front so the post-mutation compose
    // can be compared against it; when they match the write is skipped, so a
    // no-op mutate (AddTagsAsync with already-present tags, SetFieldsAsync to
    // the current values) does not trigger a watcher event or downstream
    // resource refresh. A missing storage file is either created
    // (createIfMissing=true) or quietly skipped (createIfMissing=false); a
    // Broken sidecar fails rather than being overwritten with fresh content. A
    // mutation that leaves no fields deletes the now-blank sidecar instead of
    // writing an empty file.
    //
    // The returned outcome lets callers distinguish a freshly-created sidecar
    // (registry needs to learn about the new file) from an in-place update
    // (registry already tracks the file; classification unchanged), from a
    // deletion (the emptied file was removed; registry must drop it), and from
    // a no-op (nothing happened on disk).
    private async Task<Result<SidecarWriteOutcome>> MutateFieldsAsync(
        ResourceKey resource,
        Action<Dictionary<string, object>> mutate,
        bool createIfMissing = true)
    {
        var resolveResult = ResolveSidecarKey(resource);
        if (resolveResult.IsFailure)
        {
            return Result<SidecarWriteOutcome>.Fail(resolveResult);
        }
        var sidecarKey = resolveResult.Value;

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var readResult = await ReadAsync(resource);
        if (readResult.IsFailure)
        {
            return Result<SidecarWriteOutcome>.Fail(readResult);
        }
        var read = readResult.Value;

        Dictionary<string, object> fields;
        bool sidecarExists;

        switch (read.Outcome)
        {
            case SidecarReadOutcome.Healthy:
                fields = new Dictionary<string, object>(read.Content!.Fields, StringComparer.Ordinal);
                sidecarExists = true;
                break;

            case SidecarReadOutcome.NoSidecar:
                if (!createIfMissing)
                {
                    return SidecarWriteOutcome.NoChange;
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
                    return Result<SidecarWriteOutcome>.Fail(
                        $"Cannot create sidecar '{sidecarKey}': the parent file '{parentKey}' does not exist on disk. "
                        + "The .cel extension is reserved for project metadata sidecars; pass the parent resource key.");
                }
                fields = new Dictionary<string, object>(StringComparer.Ordinal);
                sidecarExists = false;
                break;

            case SidecarReadOutcome.Broken:
            default:
                return Result<SidecarWriteOutcome>.Fail($"Cannot mutate sidecar '{sidecarKey}': {read.FailureMessage ?? "parse failed"}.");
        }

        var canonicalBefore = SidecarHelper.Compose(fields);
        mutate(fields);
        var canonicalAfter = SidecarHelper.Compose(fields);

        if (string.Equals(canonicalAfter, canonicalBefore, StringComparison.Ordinal))
        {
            return SidecarWriteOutcome.NoChange;
        }

        // A sidecar emptied by the mutation is deleted rather than left as a
        // blank file, mirroring how the first field creates it. This point is
        // reached only when the content changed, so an empty result means the
        // sidecar previously held fields and therefore exists on disk.
        if (canonicalAfter.Length == 0)
        {
            var deleteResult = await resourceFileSystem.DeleteAsync(sidecarKey);
            if (deleteResult.IsFailure)
            {
                return Result<SidecarWriteOutcome>.Fail($"Failed to delete emptied sidecar '{sidecarKey}'.")
                    .WithErrors(deleteResult);
            }

            return SidecarWriteOutcome.Deleted;
        }

        var writeResult = await resourceFileSystem.WriteAllTextAsync(sidecarKey, canonicalAfter);
        if (writeResult.IsFailure)
        {
            return Result<SidecarWriteOutcome>.Fail($"Failed to write sidecar '{sidecarKey}'.")
                .WithErrors(writeResult);
        }

        return sidecarExists ? SidecarWriteOutcome.Updated : SidecarWriteOutcome.Created;
    }

    // Resolves the resource key whose file holds the fields for the given
    // resource. For a regular file this is the sibling .cel key produced by
    // GetSidecarKey. A .cel key passed directly returns unchanged — the data
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
    // MutateFieldsAsync.
    private static ResourceKey StripSidecarSuffix(ResourceKey sidecarKey)
    {
        var fullKey = sidecarKey.FullKey;
        var trimmed = fullKey.Substring(0, fullKey.Length - SidecarFile.Extension.Length);
        return new ResourceKey(trimmed);
    }
}
