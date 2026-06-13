using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_set_alias with the alias and the version it now
/// points at.
/// </summary>
public record class PackageSetAliasResult(string PackageName, string Alias, int Version);

public partial class PackageTools
{
    /// <summary>Create or move a workshop package alias (e.g. stable) to a version.</summary>
    [McpServerTool(Name = "package_set_alias")]
    [ToolAlias("package.set_alias")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> SetAlias(string packageName, string alias, int version)
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

        if (version < 1)
        {
            return ToolResponse.Error($"Invalid version: {version}. Versions are positive integers assigned by the workshop.");
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var setResult = await packageApiClient.SetAliasAsync(packageName, alias, version);
        if (setResult.IsFailure)
        {
            return ToolResponse.Error(setResult);
        }

        var result = new PackageSetAliasResult(packageName, alias, version);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
