using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace Celbridge.GenerativeAI.Commands;

public class MakeTextCommand : CommandBase, IMakeTextCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    private ILogger<MakeTextCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey DestFileResource { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public MakeTextCommand(
        ILogger<MakeTextCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var generativeAIService = _workspaceWrapper.WorkspaceService.GenerativeAIService;

        var generateResult = await generativeAIService.GenerateTextAsync(Prompt);
        if (generateResult.IsFailure)
        {
            return Result.Fail()
                .WithErrors(generateResult);
        }
        var content = generateResult.Value;

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        // Todo: Update this to support undo/redo

        var destFilePath = resourceRegistry.GetResourcePath(DestFileResource);

        var parentFolder = Path.GetDirectoryName(destFilePath);
        if (!Directory.Exists(parentFolder))
        {
            Directory.CreateDirectory(parentFolder!);
        }

        if (File.Exists(destFilePath))
        {
            File.Delete(destFilePath);
        }

        File.WriteAllText(destFilePath, content);

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void MakeText(ResourceKey destFileResource, string prompt)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IMakeTextCommand>(command =>
        {
            command.DestFileResource = destFileResource;
            command.Prompt = prompt;
        });
    }
}
