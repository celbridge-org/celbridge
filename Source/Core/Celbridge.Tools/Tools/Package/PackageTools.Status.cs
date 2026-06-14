using System.Text.Json;
using Celbridge.Packages;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// A loaded project package in the package_status result. Version is null when
/// it cannot be read from the package's HISTORY.md (e.g. a hand-authored package
/// that was never installed from a workshop).
/// </summary>
public record class PackageStatusEntry(string Name, int? Version, string Folder);

/// <summary>
/// A package that failed to load in the package_status result, with the folder
/// the manifest lives in and the reason it was rejected.
/// </summary>
public record class PackageStatusFailure(string? Name, string Folder, string Reason, string? Detail);

/// <summary>
/// Result returned by package_status: the discovered project packages and any
/// load failures (including duplicate-name faults).
/// </summary>
public record class PackageStatusResult(
    IReadOnlyList<PackageStatusEntry> Packages,
    IReadOnlyList<PackageStatusFailure> Failures);

public partial class PackageTools
{
    /// <summary>Report the project's installed packages, their versions and folders, and any load failures.</summary>
    [McpServerTool(Name = "package_status", ReadOnly = true)]
    [ToolAlias("package.status")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> Status()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return ToolResponse.Error("No project is loaded. Open a project before checking package status.");
        }

        var workspaceService = workspaceWrapper.WorkspaceService;
        var packageService = workspaceService.PackageService;
        var resourceService = workspaceService.ResourceService;
        var resourceRegistry = resourceService.Registry;
        var resourceFileSystem = resourceService.FileSystem;

        var packages = new List<PackageStatusEntry>();
        foreach (var package in packageService.GetAllPackages())
        {
            // Only project packages participate in discovery. Bundled packages
            // ship inside the app and are not part of the project's state.
            if (package.Info.Origin != PackageOrigin.Project)
            {
                continue;
            }

            var folderKeyResult = resourceRegistry.GetResourceKey(package.Info.PackageFolder);
            if (folderKeyResult.IsFailure)
            {
                continue;
            }
            var folderKey = folderKeyResult.Value;

            var version = await TryReadInstalledVersionAsync(resourceFileSystem, folderKey);
            packages.Add(new PackageStatusEntry(package.Info.Name, version, folderKey.ToString()));
        }

        packages.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));

        var failures = new List<PackageStatusFailure>();
        foreach (var failure in packageService.GetLoadFailures())
        {
            // Only project-tree failures are actionable for the user. A bundled
            // package failure is a first-party build issue, and its folder is not
            // a project resource, so it is skipped here.
            var failureKeyResult = resourceRegistry.GetResourceKey(failure.Folder);
            if (failureKeyResult.IsFailure)
            {
                continue;
            }

            failures.Add(new PackageStatusFailure(
                failure.PackageName,
                failureKeyResult.Value.ToString(),
                failure.Reason.ToString(),
                failure.Detail));
        }

        var result = new PackageStatusResult(packages.AsReadOnly(), failures.AsReadOnly());
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
