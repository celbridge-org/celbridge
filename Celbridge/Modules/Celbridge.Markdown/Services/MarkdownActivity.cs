using Celbridge.Activities;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Markdown.ComponentEditors;
using Celbridge.Workspace;

using Path = System.IO.Path;

namespace Celbridge.Markdown.Services;

public class MarkdownActivity : IActivity
{
    public const string ActivityName = "Markdown";

    private readonly IServiceProvider _serviceProvider;
    private readonly IEntityService _entityService;
    private readonly IDocumentsService _documentsService;

    public MarkdownActivity(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        _serviceProvider = serviceProvider;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public async Task<Result> ActivateAsync()
    {
        // Register the Markdown preview provider
        var markdownProvider = _serviceProvider.AcquireService<MarkdownPreviewProvider>();
        var addMarkdownResult = _documentsService.AddPreviewProvider(".md", markdownProvider);
        if (addMarkdownResult.IsFailure)
        {
            return Result.Fail("Failed to add Markdown preview provider.")
                .WithErrors(addMarkdownResult);
        }

        // Register the AsciiDoc preview provider
        var asciiDocProvider = _serviceProvider.AcquireService<AsciiDocPreviewProvider>();
        var addAsciiDocResult = _documentsService.AddPreviewProvider(".adoc", asciiDocProvider);
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

    public Result AnnotateEntity(ResourceKey entity, IEntityAnnotation entityAnnotation)
    {
        var getComponents = _entityService.GetComponents(entity);
        if (getComponents.IsFailure)
        {
            return Result.Fail(entity, $"Failed to get entity components: '{entity}'")
                .WithErrors(getComponents);
        }
        var components = getComponents.Value;

        if (components.Count != entityAnnotation.ComponentAnnotationCount)
        {
            return Result.Fail(entity, $"Component count does not match annotation count: '{entity}'");
        }

        //
        // Root component must be "Markdown"
        //

        var rootComponent = components[0];
        if (rootComponent.Schema.ComponentType == MarkdownEditor.ComponentType)
        {
            entityAnnotation.SetIsRecognized(0);
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateResourceContentAsync(ResourceKey fileResource, IEntityAnnotation entityAnnotation)
    {
        await Task.CompletedTask;

        return Result.Ok();
    }
}
