using System.Text;
using System.Text.RegularExpressions;
using Celbridge.Commands;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Inspects sidecar health for a scope of resources. Scope is resolved from
/// Resources and Pattern: both empty means whole project, Resources only
/// checks those keys, Pattern only matches via glob, and both together is
/// the union. SummaryOnly trims per-record payloads while keeping counts.
/// </summary>
public sealed class InspectCommand : CommandBase, IInspectCommand
{
    // Records what a walk of the project found. Files lists every file the walk found, and FilesWithSidecars the
    // files that have a sidecar. Sidecars lists every sidecar, and Orphans the sidecars with no file beside them.
    // Classifying a resource listed by key looks the resource up when the walk did not find it.
    private sealed record WalkedSidecars(
        IReadOnlySet<ResourceKey> Files,
        IReadOnlySet<ResourceKey> FilesWithSidecars,
        IReadOnlySet<ResourceKey> Sidecars,
        IReadOnlySet<ResourceKey> Orphans);

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public IReadOnlyList<ResourceKey> Resources { get; set; } = Array.Empty<ResourceKey>();
    public string Pattern { get; set; } = string.Empty;
    public bool SummaryOnly { get; set; }

    public InspectResult ResultValue { get; private set; } = new InspectResult(
        Array.Empty<InspectRecord>(),
        new InspectSummary(0, 0, 0, 0, 0));

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public InspectCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // The walk, and the reads that classify each resource, run on a worker thread.
        var inspectResult = await Task.Run(InspectAsync);
        if (inspectResult.IsFailure)
        {
            return Result.Fail(inspectResult);
        }

        ResultValue = inspectResult.Value;
        return Result.Ok();
    }

    private async Task<Result<InspectResult>> InspectAsync()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        var resources = Resources ?? Array.Empty<ResourceKey>();
        bool hasResources = resources.Count > 0;
        bool hasPattern = !string.IsNullOrEmpty(Pattern);

        var walkedSidecars = new WalkedSidecars(
            new HashSet<ResourceKey>(),
            new HashSet<ResourceKey>(),
            new HashSet<ResourceKey>(),
            new HashSet<ResourceKey>());
        var scope = new HashSet<ResourceKey>();

        // Only a scope of listed keys alone needs no walk.
        if (!hasResources
            || hasPattern)
        {
            var walkResult = await resourceService.Walker.WalkAsync(ResourceKey.Empty);
            if (walkResult.IsFailure)
            {
                return Result.Fail("Failed to walk the project to inspect its sidecars.")
                    .WithErrors(walkResult);
            }

            var walkedItems = walkResult.Value;

            var fileKeys = walkedItems
                .Where(item => !item.IsFolder)
                .Select(item => item.Resource);
            var sidecarPairs = SidecarPairing.Pair(fileKeys, resourceService.Sidecars);
            walkedSidecars = BuildWalkedSidecars(sidecarPairs, resourceService.Sidecars);

            var candidates = BuildWholeProjectScope(sidecarPairs);
            if (hasPattern)
            {
                // Pattern mode filters over the same candidate set as whole-project,
                // so a `**` glob matches the same universe (parents + attention-state
                // sidecars) rather than doubling up parent and healthy-sidecar
                // entries. Match against the bare Path: PathGlobToRegex builds an
                // anchored regex that expects a path like "foo/bar.md", not the
                // canonical "project:foo/bar.md" form ToString() emits.
                var regexPattern = GlobHelper.PathGlobToRegex(Pattern);
                var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                foreach (var candidate in candidates)
                {
                    if (regex.IsMatch(candidate.Path))
                    {
                        scope.Add(candidate);
                    }
                }
            }
            else
            {
                scope.UnionWith(candidates);
            }
        }

        foreach (var resource in resources)
        {
            if (!resource.IsEmpty)
            {
                scope.Add(resource);
            }
        }

        var records = new List<InspectRecord>(scope.Count);
        foreach (var resource in scope)
        {
            var record = await ClassifyResourceAsync(resource, walkedSidecars, resourceService);
            records.Add(record);
        }

        records.Sort((a, b) => string.CompareOrdinal(a.Resource.ToString(), b.Resource.ToString()));

        var summary = BuildSummary(records);
        return new InspectResult(records, summary);
    }

    private static WalkedSidecars BuildWalkedSidecars(SidecarPairs sidecarPairs, ISidecarService sidecarService)
    {
        var files = new HashSet<ResourceKey>(sidecarPairs.Files);
        var orphans = new HashSet<ResourceKey>(sidecarPairs.Orphans);

        var sidecars = new HashSet<ResourceKey>(sidecarPairs.Orphans);
        foreach (var file in sidecarPairs.FilesWithSidecars)
        {
            var sidecarKeyResult = sidecarService.GetSidecarKey(file);
            if (sidecarKeyResult.IsSuccess)
            {
                var sidecarKey = sidecarKeyResult.Value;
                sidecars.Add(sidecarKey);
            }
        }

        return new WalkedSidecars(files, sidecarPairs.FilesWithSidecars, sidecars, orphans);
    }

    // Whole-project: every file that could have a sidecar, plus every sidecar in
    // an attention state (orphan or invalid). A paired sidecar is folded into its
    // file's record, not surfaced as a separate entry.
    private static List<ResourceKey> BuildWholeProjectScope(SidecarPairs sidecarPairs)
    {
        var scope = new List<ResourceKey>(sidecarPairs.Files);
        scope.AddRange(sidecarPairs.Orphans);
        scope.AddRange(sidecarPairs.InvalidSidecars);

        return scope;
    }

    private async Task<InspectRecord> ClassifyResourceAsync(
        ResourceKey resource,
        WalkedSidecars walkedSidecars,
        IResourceService resourceService)
    {
        if (IsSidecarKey(resource))
        {
            return await ClassifySidecarKeyAsync(resource, walkedSidecars, resourceService);
        }

        return await ClassifyParentKeyAsync(resource, walkedSidecars, resourceService.Sidecars);
    }

    private async Task<InspectRecord> ClassifySidecarKeyAsync(
        ResourceKey sidecarKey,
        WalkedSidecars walkedSidecars,
        IResourceService resourceService)
    {
        if (IsInvalidSidecarShape(sidecarKey))
        {
            return new InspectRecord(sidecarKey, SidecarStatus.InvalidSidecar, null, null, null);
        }

        if (await IsOrphanAsync(sidecarKey, walkedSidecars, resourceService))
        {
            return new InspectRecord(sidecarKey, SidecarStatus.Orphan, null, null, null);
        }

        var readResult = await resourceService.Sidecars.ReadAsync(sidecarKey);
        if (readResult.IsFailure)
        {
            return new InspectRecord(sidecarKey, SidecarStatus.NoSidecar, null, null, null);
        }
        var read = readResult.Value;

        return read.Outcome switch
        {
            SidecarReadOutcome.Healthy => BuildHealthyRecord(sidecarKey, read.Content!),
            SidecarReadOutcome.Broken => new InspectRecord(sidecarKey, SidecarStatus.Broken, null, null, read.FailureMessage),
            SidecarReadOutcome.NoSidecar => new InspectRecord(sidecarKey, SidecarStatus.NoSidecar, null, null, null),
            _ => new InspectRecord(sidecarKey, SidecarStatus.NoSidecar, null, null, null),
        };
    }

    private static async Task<bool> IsOrphanAsync(
        ResourceKey sidecarKey,
        WalkedSidecars walkedSidecars,
        IResourceService resourceService)
    {
        if (walkedSidecars.Sidecars.Contains(sidecarKey))
        {
            return walkedSidecars.Orphans.Contains(sidecarKey);
        }

        // The walk did not find this sidecar, so ask the disk. A sidecar is an orphan when the sidecar is on disk and
        // the file named for it is missing. The read of a missing sidecar reports NoSidecar.
        var sidecarInfoResult = await resourceService.FileSystem.GetInfoAsync(sidecarKey);
        if (sidecarInfoResult.IsFailure)
        {
            return false;
        }
        var sidecarInfo = sidecarInfoResult.Value;

        if (sidecarInfo.Kind != StorageItemKind.File)
        {
            return false;
        }

        // A sidecar pairs with a file only when the sidecar service builds the sidecar's own key for that file.
        var sidecarFullKey = sidecarKey.FullKey;
        var fileKeyText = sidecarFullKey.Substring(0, sidecarFullKey.Length - SidecarFile.Extension.Length);
        if (!ResourceKey.TryCreate(fileKeyText, out var fileKey))
        {
            return true;
        }

        var pairedKeyResult = resourceService.Sidecars.GetSidecarKey(fileKey);
        if (pairedKeyResult.IsFailure)
        {
            return true;
        }
        var pairedKey = pairedKeyResult.Value;

        if (pairedKey != sidecarKey)
        {
            return true;
        }

        var fileInfoResult = await resourceService.FileSystem.GetInfoAsync(fileKey);
        if (fileInfoResult.IsFailure)
        {
            return true;
        }
        var fileInfo = fileInfoResult.Value;

        return fileInfo.Kind != StorageItemKind.File;
    }

    private async Task<InspectRecord> ClassifyParentKeyAsync(
        ResourceKey parentKey,
        WalkedSidecars walkedSidecars,
        ISidecarService sidecarService)
    {
        // The walk knows which of its files have a sidecar. Only a walked file with a sidecar, or a file listed by
        // key, is read.
        if (walkedSidecars.Files.Contains(parentKey)
            && !walkedSidecars.FilesWithSidecars.Contains(parentKey))
        {
            return new InspectRecord(parentKey, SidecarStatus.NoSidecar, null, null, null);
        }

        var readResult = await sidecarService.ReadAsync(parentKey);
        if (readResult.IsFailure)
        {
            return new InspectRecord(parentKey, SidecarStatus.NoSidecar, null, null, null);
        }
        var read = readResult.Value;

        return read.Outcome switch
        {
            SidecarReadOutcome.Healthy => BuildHealthyRecord(parentKey, read.Content!),
            SidecarReadOutcome.Broken => new InspectRecord(parentKey, SidecarStatus.Broken, null, null, read.FailureMessage),
            SidecarReadOutcome.NoSidecar => new InspectRecord(parentKey, SidecarStatus.NoSidecar, null, null, null),
            _ => new InspectRecord(parentKey, SidecarStatus.NoSidecar, null, null, null),
        };
    }

    private InspectRecord BuildHealthyRecord(ResourceKey resource, SidecarContent content)
    {
        if (SummaryOnly)
        {
            return new InspectRecord(resource, SidecarStatus.Healthy, null, null, null);
        }

        var tags = ExtractTags(content);
        var fields = ExtractFieldEntries(content);
        return new InspectRecord(resource, SidecarStatus.Healthy, tags, fields, null);
    }

    private static IReadOnlyList<string> ExtractTags(SidecarContent content)
    {
        if (!content.Fields.TryGetValue(SidecarFieldNames.Tags, out var value))
        {
            return Array.Empty<string>();
        }
        return SidecarHelper.ExtractStringList(value);
    }

    private static IReadOnlyList<InspectFieldEntry> ExtractFieldEntries(SidecarContent content)
    {
        var entries = new List<InspectFieldEntry>(content.Fields.Count);
        foreach (var (name, value) in content.Fields)
        {
            if (name.StartsWith("_", StringComparison.Ordinal))
            {
                continue;
            }
            entries.Add(new InspectFieldEntry(name, EstimateFieldSize(value)));
        }
        entries.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return entries;
    }

    // Estimated UTF-8 byte count of the value's content. Lists sum recursively;
    // everything else uses ToString. Hint accuracy only — TOML framing is not
    // counted.
    private static int EstimateFieldSize(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is System.Collections.IEnumerable enumerable
            && value is not string)
        {
            int total = 0;
            foreach (var item in enumerable)
            {
                total += EstimateFieldSize(item);
            }
            return total;
        }

        return Encoding.UTF8.GetByteCount(value.ToString() ?? string.Empty);
    }

    private static InspectSummary BuildSummary(IReadOnlyList<InspectRecord> records)
    {
        int healthy = 0;
        int broken = 0;
        int orphan = 0;
        int invalid = 0;
        int noSidecar = 0;
        foreach (var record in records)
        {
            switch (record.Status)
            {
                case SidecarStatus.Healthy:
                    healthy++;
                    break;
                case SidecarStatus.Broken:
                    broken++;
                    break;
                case SidecarStatus.Orphan:
                    orphan++;
                    break;
                case SidecarStatus.InvalidSidecar:
                    invalid++;
                    break;
                case SidecarStatus.NoSidecar:
                    noSidecar++;
                    break;
            }
        }
        return new InspectSummary(healthy, broken, orphan, invalid, noSidecar);
    }

    private static bool IsSidecarKey(ResourceKey resource)
    {
        if (resource.IsEmpty)
        {
            return false;
        }
        return resource.Path.EndsWith(SidecarFile.Extension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidSidecarShape(ResourceKey resource)
    {
        var path = resource.Path;
        var doubleExtension = SidecarFile.Extension + SidecarFile.Extension;
        return path.EndsWith(doubleExtension, StringComparison.OrdinalIgnoreCase);
    }
}
