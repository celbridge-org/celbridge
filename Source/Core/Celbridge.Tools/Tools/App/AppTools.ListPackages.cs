using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A project package in the app_list_packages result, with the package version its manifest declares and
/// the resource key of its folder.
/// </summary>
public record class ProjectPackageEntry(string Name, string PackageVersion, string Folder);

/// <summary>
/// A package in the project tree that failed to load, in the app_list_packages result, with the folder the
/// manifest lives in and the reason it was rejected.
/// </summary>
public record class ProjectPackageFailure(string? Name, string Folder, string Reason, string? Detail);

/// <summary>
/// Result returned by app_list_packages: the project packages as the project loaded, and the packages in the
/// project tree that failed to load.
/// </summary>
public record class ProjectPackagesResult(
    IReadOnlyList<ProjectPackageEntry> Packages,
    IReadOnlyList<ProjectPackageFailure> Failures);

public partial class AppTools
{
    /// <summary>List the project's packages as the project loaded, with their package versions, folders and load failures.</summary>
    [McpServerTool(Name = "app_list_packages", ReadOnly = true, Idempotent = true)]
    [ToolAlias("app.list_packages")]
    [RelatedGuides("packages_overview")]
    public partial CallToolResult ListPackages()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspaceLoaded)
        {
            return ToolResponse.Error("No project is loaded. Open a project before listing its packages.");
        }

        var workspaceService = workspaceWrapper.WorkspaceService;
        var packageService = workspaceService.PackageService;
        var resourceRegistry = workspaceService.ResourceService.Registry;

        // Bundled packages ship inside the app and are not part of the project's state.
        var projectPackages = packageService.GetAllPackages()
            .Where(package => package.Info.Origin == PackageOrigin.Project)
            .OrderBy(package => package.Info.Name, StringComparer.Ordinal);

        var packages = new List<ProjectPackageEntry>();
        foreach (var package in projectPackages)
        {
            var packageVersion = package.Info.PackageVersion.ToString();
            var folder = DescribeFolder(resourceRegistry, package.Info.PackageFolder);
            packages.Add(new ProjectPackageEntry(package.Info.Name, packageVersion, folder));
        }

        // A bundled package failure is a first-party build issue, which only the load report shows.
        var projectFailures = packageService.GetLoadFailures()
            .Where(failure => failure.Origin == PackageOrigin.Project);

        var failures = new List<ProjectPackageFailure>();
        foreach (var failure in projectFailures)
        {
            var folder = DescribeFolder(resourceRegistry, failure.Folder);
            failures.Add(new ProjectPackageFailure(failure.PackageName, folder, failure.Reason.ToString(), failure.Detail));
        }

        var result = new ProjectPackagesResult(packages, failures);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    // A folder in the project tree resolves to a resource key. The folder path stands in if one ever does not,
    // so the entry is still listed and the list keeps agreeing with the app_get_state summary.
    private static string DescribeFolder(IResourceRegistry resourceRegistry, string folderPath)
    {
        var keyResult = resourceRegistry.GetResourceKey(folderPath);
        if (keyResult.IsFailure)
        {
            return folderPath;
        }

        return keyResult.Value.ToString();
    }
}
