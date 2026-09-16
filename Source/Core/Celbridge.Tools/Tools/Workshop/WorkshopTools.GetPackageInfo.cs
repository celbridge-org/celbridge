using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A workshop version entry in the workshop_get_package_info result.
/// </summary>
public record class WorkshopVersionEntry(
    int WorkshopVersion,
    string Author,
    DateTime Date,
    bool Deleted,
    string ContentHash,
    string Summary);

/// <summary>
/// An alias entry in the workshop_get_package_info result.
/// </summary>
public record class WorkshopPackageAliasEntry(string Alias, int WorkshopVersion);

/// <summary>
/// Result returned by workshop_get_package_info: a package's workshop versions and aliases.
/// </summary>
public record class WorkshopGetPackageInfoResult(
    string PackageName,
    DateTime CreatedAt,
    IReadOnlyList<WorkshopVersionEntry> WorkshopVersions,
    IReadOnlyList<WorkshopPackageAliasEntry> Aliases);

public partial class WorkshopTools
{
    /// <summary>Inspect a workshop package: its workshop versions and aliases.</summary>
    [McpServerTool(Name = "workshop_get_package_info", ReadOnly = true)]
    [ToolAlias("workshop.get_package_info")]
    [RelatedGuides("workshop_versions")]
    public async partial Task<CallToolResult> GetPackageInfo(string packageName)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var detailsResult = await packageApiClient.GetPackageAsync(packageName);
        if (detailsResult.IsFailure)
        {
            return ToolResponse.Error(detailsResult);
        }
        var details = detailsResult.Value;

        var workshopVersions = new List<WorkshopVersionEntry>();
        foreach (var workshopVersion in details.WorkshopVersions)
        {
            var entry = new WorkshopVersionEntry(
                workshopVersion.WorkshopVersion,
                workshopVersion.Author,
                workshopVersion.Date,
                workshopVersion.Deleted,
                workshopVersion.ContentHash,
                workshopVersion.Summary);
            workshopVersions.Add(entry);
        }

        var aliases = new List<WorkshopPackageAliasEntry>();
        foreach (var packageAlias in details.Aliases)
        {
            aliases.Add(new WorkshopPackageAliasEntry(packageAlias.Alias, packageAlias.WorkshopVersion));
        }

        var result = new WorkshopGetPackageInfoResult(details.Name, details.CreatedAt, workshopVersions, aliases);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
