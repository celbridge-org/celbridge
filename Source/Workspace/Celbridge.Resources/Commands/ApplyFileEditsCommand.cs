using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Commands;

public class ApplyFileEditsCommand : CommandBase, IApplyFileEditsCommand
{
    private readonly ILogger<ApplyFileEditsCommand> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    [RedactedInLogs]
    public List<FileRangeEdit> Edits { get; set; } = new();

    public ApplyFileEditsCommand(
        ILogger<ApplyFileEditsCommand> logger,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Edits.Count == 0)
        {
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;

        var failedResources = new List<ResourceKey>();
        var failureDetails = new List<string>();

        foreach (var fileEdit in Edits)
        {
            var resource = fileEdit.Resource;

            var applyResult = await ApplyEditsToDisk(resourceFileSystem, resource, fileEdit.Edits);
            if (applyResult.IsFailure)
            {
                _logger.LogWarning($"Failed to apply edits to file on disk: {resource}");
                failedResources.Add(resource);
                failureDetails.Add($"{resource}: {applyResult.FirstErrorMessage}");
            }
        }

        if (failedResources.Count > 0)
        {
            // The headline names every failed resource. The detail block below
            // names each failure's reason. The agent then learns what the
            // validator caught without retrying or reading the log.
            var headline = $"Failed to apply edits to: {string.Join(", ", failedResources)}";
            var detail = string.Join(Environment.NewLine, failureDetails);
            var errorMessage = $"{headline}{Environment.NewLine}{detail}";
            _logger.LogError(errorMessage);

            var alertTitle = _stringLocalizer.GetString("Documents_ApplyFileEditsFailedTitle");
            string alertMessage;
            if (failedResources.Count == 1)
            {
                var failedFile = failedResources[0].ToString();
                alertMessage = _stringLocalizer.GetString("Documents_ApplyFileEditsFailedSingle", failedFile);
            }
            else
            {
                alertMessage = _stringLocalizer.GetString("Documents_ApplyFileEditsFailedMultiple", failedResources.Count);
            }

            // Fire-and-forget to avoid blocking
            _ = _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);

            return Result.Fail(errorMessage);
        }

        return Result.Ok();
    }

    private static async Task<Result> ApplyEditsToDisk(
        IResourceFileSystem resourceFileSystem,
        ResourceKey resource,
        List<RangeEdit> edits)
    {
        var infoResult = await resourceFileSystem.GetInfoAsync(resource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"File not found: '{resource}'")
                .WithErrors(infoResult);
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(resource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read file: '{resource}'")
                .WithErrors(readResult);
        }
        var originalContent = readResult.Value;

        var applyResult = RangeEditHelper.ApplyEdits(originalContent, edits);
        if (applyResult.IsFailure)
        {
            return Result.Fail(applyResult);
        }
        var output = applyResult.Value;

        var writeResult = await resourceFileSystem.WriteAllTextAsync(resource, output);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write edits to file: '{resource}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }
}
