using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_remove_package_alias confirming the alias was removed.
/// </summary>
public record class WorkshopRemovePackageAliasResult(string PackageName, string Alias, bool Removed);

public partial class WorkshopTools
{
    /// <summary>Remove a workshop package alias, leaving the workshop version it pointed at unaffected.</summary>
    [McpServerTool(Name = "workshop_remove_package_alias")]
    [ToolAlias("workshop.remove_package_alias")]
    [RelatedGuides("workshop_versions")]
    public async partial Task<CallToolResult> RemovePackageAlias(string packageName, string alias)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        var aliasCheck = ValidateAlias(alias);
        if (aliasCheck.IsFailure)
        {
            return ToolResponse.Error(aliasCheck);
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var removeResult = await packageApiClient.RemoveAliasAsync(packageName, alias);
        if (removeResult.IsFailure)
        {
            return ToolResponse.Error(removeResult);
        }

        var result = new WorkshopRemovePackageAliasResult(packageName, alias, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
