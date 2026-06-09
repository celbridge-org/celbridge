using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Inspect sidecar health for one resource, many resources, or the whole project.</summary>
    [McpServerTool(Name = "data_inspect", ReadOnly = true)]
    [ToolAlias("data.inspect")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> Inspect(string resourcesJson = "", string pattern = "", bool summaryOnly = false)
    {
        var resourceKeys = new List<ResourceKey>();
        if (!string.IsNullOrWhiteSpace(resourcesJson))
        {
            var parseResult = TryParseStringArray(resourcesJson, "resources");
            if (parseResult.IsFailure)
            {
                return ToolResponse.Error(parseResult);
            }
            foreach (var keyString in parseResult.Value)
            {
                if (!ResourceKey.TryCreate(keyString, out var key))
                {
                    return ToolResponse.InvalidResourceKey(keyString);
                }
                resourceKeys.Add(key);
            }
        }

        var commandResult = await ExecuteCommandAsync<IInspectCommand, InspectResult>(command =>
        {
            command.Resources = resourceKeys;
            command.Pattern = pattern ?? string.Empty;
            command.SummaryOnly = summaryOnly;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var report = commandResult.Value;
        var resultsPayload = new List<object>(report.Records.Count);
        foreach (var record in report.Records)
        {
            resultsPayload.Add(BuildRecordPayload(record));
        }

        var payload = new
        {
            results = resultsPayload,
            summary = new
            {
                healthy = report.Summary.Healthy,
                broken = report.Summary.Broken,
                orphan = report.Summary.Orphan,
                invalidSidecar = report.Summary.InvalidSidecar,
                noSidecar = report.Summary.NoSidecar,
            },
        };
        return ToolResponse.Success(SerializeJson(payload));
    }

    private static object BuildRecordPayload(InspectRecord record)
    {
        var resourceString = record.Resource.ToString();
        var statusString = StatusToString(record.Status);

        if (record.Status == SidecarStatus.Healthy
            && record.Tags is not null
            && record.Fields is not null)
        {
            var fieldEntries = new List<object>(record.Fields.Count);
            foreach (var entry in record.Fields)
            {
                fieldEntries.Add(new { name = entry.Name, size = entry.Size });
            }
            return new
            {
                resource = resourceString,
                status = statusString,
                tags = record.Tags,
                fields = fieldEntries,
            };
        }

        if (record.Status == SidecarStatus.Broken
            && record.ParseError is not null)
        {
            return new
            {
                resource = resourceString,
                status = statusString,
                parseError = record.ParseError,
            };
        }

        return new
        {
            resource = resourceString,
            status = statusString,
        };
    }

    private static string StatusToString(SidecarStatus status)
    {
        return status switch
        {
            SidecarStatus.Healthy => "Healthy",
            SidecarStatus.Broken => "Broken",
            SidecarStatus.Orphan => "Orphan",
            SidecarStatus.InvalidSidecar => "InvalidSidecar",
            SidecarStatus.NoSidecar => "NoSidecar",
            _ => "Unknown",
        };
    }
}
