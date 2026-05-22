using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Report broken project: references, orphan sidecars, and any sidecar that fails to parse cleanly.</summary>
    [McpServerTool(Name = "data_check_project", ReadOnly = true)]
    [ToolAlias("data.check_project")]
    [RelatedGuides("resource_keys")]
    public async partial Task<CallToolResult> CheckProject()
    {
        var checkResult = await ExecuteCommandAsync<IProjectCheckCommand, ProjectCheckReport>();
        if (checkResult.IsFailure)
        {
            return ToolResponse.Error(checkResult);
        }
        var report = checkResult.Value;

        var payload = new
        {
            brokenReferences = report.BrokenReferences
                .Select(b => new
                {
                    source = b.Source.ToString(),
                    missingTarget = b.MissingTarget.ToString(),
                })
                .ToArray(),
            orphanSidecars = report.OrphanSidecars
                .Select(o => o.Sidecar.ToString())
                .ToArray(),
            brokenSidecars = report.BrokenSidecars
                .Select(b => b.Sidecar.ToString())
                .ToArray(),
        };

        return ToolResponse.Success(SerializeJson(payload));
    }
}
