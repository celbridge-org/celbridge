using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_unpublish confirming the package was removed.
/// </summary>
public record class PackageUnpublishResult(string PackageName, bool Unpublished);

public partial class PackageTools
{
    /// <summary>Unpublish a whole package and all its versions from the workshop.</summary>
    [McpServerTool(Name = "package_unpublish", Destructive = true)]
    [ToolAlias("package.unpublish")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> Unpublish(string packageName)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        var confirmed = await ConfirmUnpublishAsync(packageName);
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

        var result = new PackageUnpublishResult(packageName, true);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmUnpublishAsync(string packageName)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var title = localizerService.GetString("Package_UnpublishConfirm_Title");
        var message = localizerService.GetString("Package_UnpublishConfirm_Message", packageName);

        return await ConfirmActionAsync(title, message);
    }
}
