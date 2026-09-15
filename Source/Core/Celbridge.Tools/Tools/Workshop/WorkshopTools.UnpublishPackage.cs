using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_unpublish_package confirming the package was removed.
/// </summary>
public record class WorkshopUnpublishPackageResult(string PackageName, bool Unpublished);

public partial class WorkshopTools
{
    /// <summary>Unpublish a whole package and all its workshop versions from the workshop.</summary>
    [McpServerTool(Name = "workshop_unpublish_package", Destructive = true)]
    [ToolAlias("workshop.unpublish_package")]
    [RelatedGuides("workshop_versions")]
    public async partial Task<CallToolResult> UnpublishPackage(string packageName)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        var confirmed = await ConfirmUnpublishPackageAsync(packageName);
        if (!confirmed)
        {
            return ToolResponse.Error("Unpublish cancelled by user.");
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var unpublishResult = await packageApiClient.DeletePackageAsync(packageName);
        if (unpublishResult.IsFailure)
        {
            return ToolResponse.Error(unpublishResult);
        }

        var result = new WorkshopUnpublishPackageResult(packageName, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmUnpublishPackageAsync(string packageName)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var title = localizerService.GetString("Workshop_UnpublishPackageConfirm_Title");
        var message = localizerService.GetString("Workshop_UnpublishPackageConfirm_Message", packageName);

        return await ConfirmActionAsync(title, message);
    }
}
