using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A package entry in the package_list result. LatestVersion and PublishedAt
/// are null when the package has no live versions.
/// </summary>
public record class PackageListEntry(string PackageName, int? LatestVersion, DateTime? PublishedAt, int VersionsCount);

public partial class PackageTools
{
    /// <summary>List all packages available in the connected workshop.</summary>
    [McpServerTool(Name = "package_list", ReadOnly = true)]
    [ToolAlias("package.list")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> List()
    {
        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var listResult = await packageApiClient.ListPackagesAsync();

        if (listResult.IsFailure)
        {
            return ToolResponse.Error(listResult);
        }

        var packages = new List<PackageListEntry>();
        foreach (var package in listResult.Value)
        {
            var entry = new PackageListEntry(
                package.Name,
                package.LatestVersion?.Version,
                package.LatestVersion?.Date,
                package.VersionsCount);
            packages.Add(entry);
        }

        var json = JsonSerializer.Serialize(packages, JsonOptions);
        return ToolResponse.Success(json);
    }
}
