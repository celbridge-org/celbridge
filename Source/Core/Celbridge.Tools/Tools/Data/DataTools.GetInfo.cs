using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Return a resource's .cel sidecar field set inline.</summary>
    [McpServerTool(Name = "data_get_info", ReadOnly = true)]
    [ToolAlias("data.get_info")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> GetInfo(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }
        var sidecarError = ValidateNotSidecarKey(resourceKey, resource);
        if (sidecarError is not null)
        {
            return sidecarError;
        }

        var commandResult = await ExecuteCommandAsync<IGetInfoCommand, GetInfoResult>(command =>
        {
            command.Resource = resourceKey;
        });
        if (commandResult.IsFailure)
        {
            return ToolResponse.Error(commandResult);
        }

        var report = commandResult.Value;
        var payload = new
        {
            hasSidecar = report.HasSidecar,
            fields = report.Fields,
        };
        return ToolResponse.Success(SerializeJson(payload));
    }
}
