using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_delete confirming the version was deleted.
/// </summary>
public record class PackageDeleteResult(string PackageName, int Version, bool Deleted);

public partial class PackageTools
{
    /// <summary>Delete a published package version from the workshop, removing its content permanently.</summary>
    [McpServerTool(Name = "package_delete", Destructive = true)]
    [ToolAlias("package.delete")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> Delete(string packageName, string version)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            return ToolResponse.Error("A version number or alias is required: package_delete has no default target.");
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();

        var detailsResult = await packageApiClient.GetPackageAsync(packageName);
        if (detailsResult.IsFailure)
        {
            return ToolResponse.Error(detailsResult);
        }
        var packageDetails = detailsResult.Value;

        var resolveResult = PackageVersionResolver.ResolveForDelete(packageDetails, version.Trim());
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var resolvedVersion = resolveResult.Value;

        // Aliases pointing at the deleted version are left dangling (or repointed
        // by the server, depending on the alias), so the confirmation names them.
        var danglingAliases = packageDetails.Aliases
            .Where(packageAlias => packageAlias.Version == resolvedVersion)
            .Select(packageAlias => packageAlias.Alias)
            .ToList();

        var confirmed = await ConfirmDeleteVersionAsync(packageName, resolvedVersion, danglingAliases);
        if (!confirmed)
        {
            return ToolResponse.Error("Delete cancelled by user.");
        }

        var deleteResult = await packageApiClient.DeleteVersionAsync(packageName, resolvedVersion);
        if (deleteResult.IsFailure)
        {
            return ToolResponse.Error(deleteResult);
        }

        var result = new PackageDeleteResult(packageName, resolvedVersion, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmDeleteVersionAsync(string packageName, int version, IReadOnlyList<string> danglingAliases)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();

        var title = localizerService.GetString("Package_DeleteConfirm_Title");

        string message;
        if (danglingAliases.Count > 0)
        {
            var aliasList = string.Join(", ", danglingAliases);
            message = localizerService.GetString("Package_DeleteConfirm_MessageWithAliases", version, packageName, aliasList);
        }
        else
        {
            message = localizerService.GetString("Package_DeleteConfirm_Message", version, packageName);
        }

        return await ConfirmActionAsync(title, message);
    }
}
