using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Find every dangling "project:..." reference in the workspace.</summary>
    [McpServerTool(Name = "data_check_references", ReadOnly = true)]
    [ToolAlias("data.check_references")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> CheckReferences()
    {
        var commandResult = await ExecuteCommandAsync<IProjectCheckCommand, ProjectCheckReport>();
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }
        var report = commandResult.Value;

        var referencesPayload = report.BrokenReferences
            .Select(entry => new
            {
                source = entry.Source.ToString(),
                missingTarget = entry.MissingTarget.ToString(),
            })
            .ToArray();

        var payload = new
        {
            references = referencesPayload,
        };
        return ToolResponse.Success(SerializeJson(payload));
    }
}
