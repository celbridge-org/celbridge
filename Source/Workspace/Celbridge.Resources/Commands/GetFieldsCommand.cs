using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

/// <summary>
/// Reads a batch of fields through the sidecar data service. Missing fields
/// surface as Found=false rather than failing the batch. The sentinel "*"
/// expands to every field on the sidecar.
/// SuppressCommandLog because reads should not clutter the command log.
/// </summary>
public sealed class GetFieldsCommand : CommandBase, IGetFieldsCommand
{
    private const string AllFieldsSentinel = "*";

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }
    public IReadOnlyList<string> Names { get; set; } = Array.Empty<string>();

    public IReadOnlyList<GetFieldResult> ResultValue { get; private set; } = Array.Empty<GetFieldResult>();

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public GetFieldsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Names is null
            || Names.Count == 0)
        {
            return Result.Fail("Names list must contain at least one entry.");
        }

        var sidecarService = _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;
        var readResult = await sidecarService.ReadAsync(Resource);
        if (readResult.IsFailure)
        {
            return Result.Fail(readResult);
        }
        var read = readResult.Value;

        if (read.Outcome == SidecarReadOutcome.NoSidecar)
        {
            return Result.Fail($"Resource '{Resource}' has no sidecar.");
        }
        if (read.Outcome == SidecarReadOutcome.Broken)
        {
            return Result.Fail($"Sidecar for resource '{Resource}' is broken: {read.FailureMessage}");
        }

        var fields = read.Content!.Fields;

        if (Names.Count == 1
            && string.Equals(Names[0], AllFieldsSentinel, StringComparison.Ordinal))
        {
            var expanded = new List<GetFieldResult>(fields.Count);
            foreach (var (name, value) in fields)
            {
                if (IsReservedName(name))
                {
                    continue;
                }
                expanded.Add(new GetFieldResult(name, true, value));
            }
            expanded.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            ResultValue = expanded;
            return Result.Ok();
        }

        var results = new List<GetFieldResult>(Names.Count);
        foreach (var name in Names)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Result.Fail("Field name is empty.");
            }
            if (IsReservedName(name))
            {
                results.Add(new GetFieldResult(name, false, null));
                continue;
            }
            if (fields.TryGetValue(name, out var value))
            {
                results.Add(new GetFieldResult(name, true, value));
            }
            else
            {
                results.Add(new GetFieldResult(name, false, null));
            }
        }

        ResultValue = results;
        return Result.Ok();
    }

    private static bool IsReservedName(string name)
    {
        return name.StartsWith("_", StringComparison.Ordinal);
    }
}
