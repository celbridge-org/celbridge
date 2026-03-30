using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// A package entry in the package_list result.
/// </summary>
public record class PackageListEntry(string PackageName, long Size, DateTime UploadedAt);

public partial class PackageTools
{
    /// <summary>
    /// Lists all packages available in the remote package registry.
    /// </summary>
    /// <returns>JSON array of objects with fields: packageName (string), size (long), uploadedAt (datetime).</returns>
    [McpServerTool(Name = "package_list", ReadOnly = true)]
    [ToolAlias("package.list")]
    public async partial Task<CallToolResult> List()
    {
        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var listResult = await packageApiClient.ListPackagesAsync();

        if (listResult.IsFailure)
        {
            return ErrorResult(listResult.Error);
        }

        var packages = new List<PackageListEntry>();
        foreach (var entry in listResult.Value)
        {
            if (entry.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var packageName = Path.GetFileNameWithoutExtension(entry.FileName);
                if (IsValidPackageName(packageName))
                {
                    packages.Add(new PackageListEntry(packageName, entry.FileSize, entry.UploadedAt));
                }
            }
        }

        var json = JsonSerializer.Serialize(packages, JsonOptions);
        return SuccessResult(json);
    }
}
