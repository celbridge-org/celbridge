using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_delete_package confirming the workshop version was deleted.
/// </summary>
public record class WorkshopDeletePackageResult(string PackageName, int WorkshopVersion, bool Deleted);

public partial class WorkshopTools
{
    /// <summary>Delete one published workshop version of a package, removing its content permanently.</summary>
    [McpServerTool(Name = "workshop_delete_package", Destructive = true)]
    [ToolAlias("workshop.delete_package")]
    [RelatedGuides("workshop_versions")]
    public async partial Task<CallToolResult> DeletePackage(string packageName, string workshopVersion)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        if (string.IsNullOrWhiteSpace(workshopVersion))
        {
            return ToolResponse.Error("A workshop version number or alias is required. workshop_delete_package has no default target.");
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();

        var detailsResult = await packageApiClient.GetPackageAsync(packageName);
        if (detailsResult.IsFailure)
        {
            return ToolResponse.Error(detailsResult);
        }
        var packageDetails = detailsResult.Value;

        var resolveResult = WorkshopVersionResolver.Resolve(packageDetails, workshopVersion.Trim());
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult);
        }
        var resolvedWorkshopVersion = resolveResult.Value;

        // Aliases pointing at the deleted workshop version are left dangling (or
        // repointed by the server, depending on the alias), so the confirmation names them.
        var danglingAliases = packageDetails.Aliases
            .Where(packageAlias => packageAlias.WorkshopVersion == resolvedWorkshopVersion)
            .Select(packageAlias => packageAlias.Alias)
            .ToList();

        var confirmed = await ConfirmDeleteWorkshopVersionAsync(packageName, resolvedWorkshopVersion, danglingAliases);
        if (!confirmed)
        {
            return ToolResponse.Error("Delete cancelled by user.");
        }

        var deleteResult = await packageApiClient.DeleteVersionAsync(packageName, resolvedWorkshopVersion);
        if (deleteResult.IsFailure)
        {
            return ToolResponse.Error(deleteResult);
        }

        var result = new WorkshopDeletePackageResult(packageName, resolvedWorkshopVersion, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmDeleteWorkshopVersionAsync(string packageName, int workshopVersion, IReadOnlyList<string> danglingAliases)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();

        var title = localizerService.GetString("Workshop_DeletePackageConfirm_Title");

        string message;
        if (danglingAliases.Count > 0)
        {
            var aliasList = string.Join(", ", danglingAliases);
            message = localizerService.GetString("Workshop_DeletePackageConfirm_MessageWithAliases", workshopVersion, packageName, aliasList);
        }
        else
        {
            message = localizerService.GetString("Workshop_DeletePackageConfirm_Message", workshopVersion, packageName);
        }

        return await ConfirmActionAsync(title, message);
    }
}
