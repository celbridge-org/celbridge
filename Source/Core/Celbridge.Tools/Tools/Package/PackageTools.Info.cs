using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A version entry in the package_info result.
/// </summary>
public record class PackageVersionEntry(
    int Version,
    string Author,
    DateTime Date,
    bool Tombstoned,
    string ContentHash,
    string Summary);

/// <summary>
/// An alias entry in the package_info result.
/// </summary>
public record class PackageAliasEntry(string Alias, int Version);

/// <summary>
/// Result returned by package_info: a package's versions and aliases.
/// </summary>
public record class PackageInfoResult(
    string PackageName,
    DateTime CreatedAt,
    IReadOnlyList<PackageVersionEntry> Versions,
    IReadOnlyList<PackageAliasEntry> Aliases);

public partial class PackageTools
{
    /// <summary>Inspect a workshop package: its versions and aliases.</summary>
    [McpServerTool(Name = "package_info", ReadOnly = true)]
    [ToolAlias("package.info")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> Info(string packageName)
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

        var versions = new List<PackageVersionEntry>();
        foreach (var packageVersion in details.Versions)
        {
            var entry = new PackageVersionEntry(
                packageVersion.Version,
                packageVersion.Author,
                packageVersion.Date,
                packageVersion.Tombstoned,
                packageVersion.ContentHash,
                packageVersion.Summary);
            versions.Add(entry);
        }

        var aliases = new List<PackageAliasEntry>();
        foreach (var packageAlias in details.Aliases)
        {
            aliases.Add(new PackageAliasEntry(packageAlias.Alias, packageAlias.Version));
        }

        var result = new PackageInfoResult(details.Name, details.CreatedAt, versions, aliases);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
