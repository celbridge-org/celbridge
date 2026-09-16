using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A package entry in the workshop_list_packages result. LatestWorkshopVersion and PublishedAt
/// are null when the package has no live workshop versions.
/// </summary>
public record class WorkshopListPackagesEntry(string PackageName, int? LatestWorkshopVersion, DateTime? PublishedAt, int WorkshopVersionCount);

public partial class WorkshopTools
{
    /// <summary>List all packages available in the connected workshop.</summary>
    [McpServerTool(Name = "workshop_list_packages", ReadOnly = true)]
    [ToolAlias("workshop.list_packages")]
    [RelatedGuides("workshop_versions")]
    public async partial Task<CallToolResult> ListPackages()
    {
        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var listResult = await packageApiClient.ListPackagesAsync();

        if (listResult.IsFailure)
        {
            return ToolResponse.Error(listResult);
        }

        var packages = new List<WorkshopListPackagesEntry>();
        foreach (var package in listResult.Value)
        {
            var entry = new WorkshopListPackagesEntry(
                package.Name,
                package.LatestWorkshopVersion?.WorkshopVersion,
                package.LatestWorkshopVersion?.Date,
                package.WorkshopVersionCount);
            packages.Add(entry);
        }

        var json = JsonSerializer.Serialize(packages, JsonOptions);
        return ToolResponse.Success(json);
    }
}
