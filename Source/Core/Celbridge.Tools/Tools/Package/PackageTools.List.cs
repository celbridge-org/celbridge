using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Directory = System.IO.Directory;
using FileInfo = System.IO.FileInfo;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// A package entry in the package_list result.
/// </summary>
public record class PackageListEntry(string PackageName, long Size);

public partial class PackageTools
{
    /// <summary>
    /// Lists all packages available in the local package registry.
    /// </summary>
    /// <returns>JSON array of objects with fields: packageName (string), size (long).</returns>
    [McpServerTool(Name = "package_list", ReadOnly = true)]
    [ToolAlias("package.list")]
    public partial CallToolResult List()
    {
        var registryPath = GetPackageRegistryPath();

        var packages = new List<PackageListEntry>();

        if (Directory.Exists(registryPath))
        {
            var zipFiles = Directory.GetFiles(registryPath, "*.zip");

            foreach (var zipFile in zipFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(zipFile);

                if (!IsValidPackageName(fileName))
                {
                    continue;
                }

                var fileInfo = new FileInfo(zipFile);
                packages.Add(new PackageListEntry(fileName, fileInfo.Length));
            }
        }

        var json = JsonSerializer.Serialize(packages, JsonOptions);
        return SuccessResult(json);
    }
}
