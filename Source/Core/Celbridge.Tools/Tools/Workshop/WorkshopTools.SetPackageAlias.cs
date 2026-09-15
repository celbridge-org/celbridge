using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_set_package_alias with the alias and the workshop version
/// it now points at.
/// </summary>
public record class WorkshopSetPackageAliasResult(string PackageName, string Alias, int WorkshopVersion);

public partial class WorkshopTools
{
    /// <summary>Create or move a workshop package alias (e.g. stable) to a workshop version.</summary>
    [McpServerTool(Name = "workshop_set_package_alias")]
    [ToolAlias("workshop.set_package_alias")]
    [RelatedGuides("workshop_versions")]
    public async partial Task<CallToolResult> SetPackageAlias(string packageName, string alias, int workshopVersion)
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

        if (workshopVersion < 1)
        {
            return ToolResponse.Error($"Invalid workshop version: {workshopVersion}. Workshop versions are positive integers assigned by the workshop.");
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var setResult = await packageApiClient.SetAliasAsync(packageName, alias, workshopVersion);
        if (setResult.IsFailure)
        {
            return ToolResponse.Error(setResult);
        }

        var result = new WorkshopSetPackageAliasResult(packageName, alias, workshopVersion);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
