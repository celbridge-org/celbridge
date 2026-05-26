using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class DataTools
{
    /// <summary>Report broken project: references, orphan .cel files, and any .cel file that fails to parse cleanly.</summary>
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
            orphanCelFiles = report.OrphanCelFiles
                .Select(o => o.ToString())
                .ToArray(),
            brokenCelFiles = report.BrokenCelFiles
                .Select(b => b.ToString())
                .ToArray(),
        };

        return ToolResponse.Success(SerializeJson(payload));
    }
}
