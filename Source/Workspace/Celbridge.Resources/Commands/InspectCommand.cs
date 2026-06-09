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
    // Compact tag for an entry inside the registry's SidecarReport. Used by
    // the per-resource classifier to look up a sidecar key's reported state
    // (the registry partitions .cel files into Healthy / Broken / Orphan
    // lists; this enum is the union of those three buckets).
    private enum SidecarReportEntry
    {
        Healthy,
        Broken,
        Orphan,
    }

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
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var registry = resourceService.Registry;
        var sidecarService = resourceService.Sidecars;
        var sidecarReport = registry.GetSidecarReport();

        var sidecarStatusByKey = BuildSidecarStatusIndex(sidecarReport);

        var scope = BuildScope(registry, sidecarReport, sidecarStatusByKey);

        var records = new List<InspectRecord>(scope.Count);
        foreach (var resource in scope)
        {
            var record = await ClassifyResourceAsync(
                resource,
                sidecarService,
                sidecarStatusByKey);
            records.Add(record);
        }

        records.Sort((a, b) => string.CompareOrdinal(a.Resource.ToString(), b.Resource.ToString()));

        var summary = BuildSummary(records);
        ResultValue = new InspectResult(records, summary);
        return Result.Ok();
    }

    private List<ResourceKey> BuildScope(
        IResourceRegistry registry,
        SidecarReport sidecarReport,
        Dictionary<ResourceKey, SidecarReportEntry> sidecarStatusByKey)
    {
        var resources = Resources ?? Array.Empty<ResourceKey>();
        bool hasResources = resources.Count > 0;
        bool hasPattern = !string.IsNullOrEmpty(Pattern);

        if (!hasResources
            && !hasPattern)
        {
            return BuildWholeProjectScope(registry, sidecarReport);
        }

        var scopeSet = new HashSet<ResourceKey>();
        if (hasResources)
        {
            foreach (var resource in resources)
            {
                if (!resource.IsEmpty)
                {
                    scopeSet.Add(resource);
                }
            }
        }

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
            var candidates = BuildWholeProjectScope(registry, sidecarReport);
            foreach (var candidate in candidates)
            {
                if (regex.IsMatch(candidate.Path))
                {
                    scopeSet.Add(candidate);
                }
            }
        }

        return scopeSet.ToList();
    }

    private List<ResourceKey> BuildWholeProjectScope(
        IResourceRegistry registry,
        SidecarReport sidecarReport)
    {
        // Whole-project: every parent file that could have a sidecar (i.e.
        // non-.cel files), plus every sidecar key in an attention state
        // (orphan, broken, or invalid). Healthy sidecars are folded into
        // their parent records, not surfaced as separate entries.
        var scopeSet = new HashSet<ResourceKey>();

        foreach (var entry in registry.GetAllFileResources())
        {
            var key = entry.Resource;
            if (IsSidecarKey(key))
            {
                continue;
            }
            scopeSet.Add(key);
        }

        foreach (var orphan in sidecarReport.Orphan)
        {
            scopeSet.Add(orphan);
        }
        foreach (var broken in sidecarReport.Broken)
        {
            // .cel.cel files and parse-broken sidecars without a paired parent
            // surface against their own key; parse-broken sidecars with an
            // on-disk parent surface against the parent and are picked up by
            // the parent loop above.
            if (IsInvalidSidecarShape(broken))
            {
                scopeSet.Add(broken);
                continue;
            }
            var parent = StripSidecarSuffix(broken);
            if (parent is null)
            {
                scopeSet.Add(broken);
                continue;
            }
            // The parent file is in the registry's file list when present;
            // when absent the registry entry won't exist, so the broken
            // sidecar surfaces against its own key for visibility.
            if (!scopeSet.Contains(parent.Value))
            {
                scopeSet.Add(broken);
            }
        }

        return scopeSet.ToList();
    }

    private async Task<InspectRecord> ClassifyResourceAsync(
        ResourceKey resource,
        ISidecarService sidecarService,
        Dictionary<ResourceKey, SidecarReportEntry> sidecarStatusByKey)
    {
        if (IsSidecarKey(resource))
        {
            return await ClassifySidecarKeyAsync(resource, sidecarService, sidecarStatusByKey);
        }

        return await ClassifyParentKeyAsync(resource, sidecarService, sidecarStatusByKey);
    }

    private async Task<InspectRecord> ClassifySidecarKeyAsync(
        ResourceKey sidecarKey,
        ISidecarService sidecarService,
        Dictionary<ResourceKey, SidecarReportEntry> sidecarStatusByKey)
    {
        if (IsInvalidSidecarShape(sidecarKey))
        {
            return new InspectRecord(sidecarKey, SidecarStatus.InvalidSidecar, null, null, null);
        }

        if (sidecarStatusByKey.TryGetValue(sidecarKey, out var entry)
            && entry == SidecarReportEntry.Orphan)
        {
            return new InspectRecord(sidecarKey, SidecarStatus.Orphan, null, null, null);
        }

        var readResult = await sidecarService.ReadAsync(sidecarKey);
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

    private async Task<InspectRecord> ClassifyParentKeyAsync(
        ResourceKey parentKey,
        ISidecarService sidecarService,
        Dictionary<ResourceKey, SidecarReportEntry> sidecarStatusByKey)
    {
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

    private static Dictionary<ResourceKey, SidecarReportEntry> BuildSidecarStatusIndex(SidecarReport report)
    {
        var index = new Dictionary<ResourceKey, SidecarReportEntry>();
        foreach (var key in report.Healthy)
        {
            index[key] = SidecarReportEntry.Healthy;
        }
        foreach (var key in report.Broken)
        {
            index[key] = SidecarReportEntry.Broken;
        }
        foreach (var key in report.Orphan)
        {
            index[key] = SidecarReportEntry.Orphan;
        }
        return index;
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

    private static ResourceKey? StripSidecarSuffix(ResourceKey sidecarKey)
    {
        var fullKey = sidecarKey.FullKey;
        if (!fullKey.EndsWith(SidecarFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var trimmed = fullKey.Substring(0, fullKey.Length - SidecarFile.Extension.Length);
        if (string.IsNullOrEmpty(trimmed)
            || trimmed.EndsWith(":", StringComparison.Ordinal))
        {
            return null;
        }
        return new ResourceKey(trimmed);
    }
}
