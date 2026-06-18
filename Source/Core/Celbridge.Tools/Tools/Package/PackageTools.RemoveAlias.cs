using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_remove_alias confirming the alias was removed.
/// </summary>
public record class PackageRemoveAliasResult(string PackageName, string Alias, bool Removed);

public partial class PackageTools
{
    /// <summary>Remove a workshop package alias; the version it pointed at is unaffected.</summary>
    [McpServerTool(Name = "package_remove_alias")]
    [ToolAlias("package.remove_alias")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> RemoveAlias(string packageName, string alias)
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

        var result = new PackageRemoveAliasResult(packageName, alias, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
