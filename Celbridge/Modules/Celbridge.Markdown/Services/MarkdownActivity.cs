using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Markdown.Components;
using Celbridge.Workspace;

using Path = System.IO.Path;

namespace Celbridge.Markdown.Services;

public class MarkdownActivity : IActivity
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MarkdownActivity> _logger;
    private readonly IEntityService _entityService;
    private readonly IDocumentsService _documentsService;

    public MarkdownActivity(
        IServiceProvider serviceProvider,
        ILogger<MarkdownActivity> logger,        
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public async Task<Result> ActivateAsync()
    {
        // Register the Markdown preview provider
        var markdownProvider = _serviceProvider.AcquireService<MarkdownPreviewProvider>();
        var addMarkdownResult = _documentsService.AddPreviewProvider(markdownProvider);
        if (addMarkdownResult.IsFailure)
        {
            return Result.Fail("Failed to add Markdown preview provider.")
                .WithErrors(addMarkdownResult);
        }

        // Register the AsciiDoc preview provider
        var asciiDocProvider = _serviceProvider.AcquireService<AsciiDocPreviewProvider>();
        var addAsciiDocResult = _documentsService.AddPreviewProvider(asciiDocProvider);
        if (addAsciiDocResult.IsFailure)
        {
            return Result.Fail("Failed to add Asciidoc preview provider.")
                .WithErrors(addAsciiDocResult);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public async Task<Result> DeactivateAsync()
    {
        await Task.CompletedTask;
        return Result.Ok();
    }

    public bool SupportsResource(ResourceKey resource)
    {
        var extension = Path.GetExtension(resource);
        return extension == ".md";
    }

    public async Task<Result> InitializeResourceAsync(ResourceKey resource)
    {
        if (!SupportsResource(resource))
        {
            return Result.Fail($"This activity does not support this resource: {resource}");
        }

        var count = _entityService.GetComponentCount(resource);
        if (count > 0)
        {
            // Entity has already been initialized
            return Result.Ok();
        }

        _entityService.AddComponent(new ComponentKey(resource, 0), MarkdownEditor.ComponentType);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public Result UpdateEntityAnnotation(ResourceKey resource, IEntityAnnotation entityAnnotation)
    {
        return Result.Ok();
    }

    public async Task<Result> UpdateResourceAsync(ResourceKey fileResource)
    {
        await Task.CompletedTask;

        return Result.Ok();
    }
}
