using Celbridge.Commands;
using Celbridge.Packages;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class OpenUtilityCommand : CommandBase, IOpenUtilityCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public string UtilityId { get; set; } = string.Empty;

    public OpenUtilityCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (string.IsNullOrEmpty(UtilityId))
        {
            return Result.Fail("Cannot open utility document: UtilityId is empty");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var packageService = workspaceService.PackageService;

        var utilityContribution = FindUtilityContribution(packageService, UtilityId);
        if (utilityContribution is null)
        {
            return Result.Fail($"No registered utility document found with id '{UtilityId}'");
        }

        var descriptor = utilityContribution.UtilityDescriptor;
        Guard.IsNotNull(descriptor);

        if (!ResourceKey.TryCreate(descriptor.Resource, out var resourceKey))
        {
            return Result.Fail($"Utility '{UtilityId}' declares an invalid resource: '{descriptor.Resource}'");
        }

        var editorId = new DocumentEditorId($"{utilityContribution.Package.Name}.{utilityContribution.Id}");
        var options = new OpenDocumentOptions(Activate: true, EditorId: editorId);

        var openResult = await workspaceService.DocumentsService.OpenDocument(resourceKey, options);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to open utility document '{UtilityId}'")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    private static CustomDocumentEditorContribution? FindUtilityContribution(IPackageService packageService, string utilityId)
    {
        foreach (var contribution in packageService.GetAllDocumentEditors())
        {
            if (contribution is not CustomDocumentEditorContribution { IsUtility: true } custom)
            {
                continue;
            }

            var editorId = $"{custom.Package.Name}.{custom.Id}";
            if (string.Equals(editorId, utilityId, StringComparison.Ordinal))
            {
                return custom;
            }
        }

        return null;
    }
}
