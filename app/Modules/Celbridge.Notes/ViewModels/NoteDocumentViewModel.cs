using System.Text;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Notes.ViewModels;

public partial class NoteDocumentViewModel : DocumentViewModel
{
    private readonly IMessengerService _messengerService;
    private readonly IFileTemplateService _fileTemplateService;
    private readonly IResourceRegistry _resourceRegistry;

    public NoteDocumentViewModel(
        IMessengerService messengerService,
        IFileTemplateService fileTemplateService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _fileTemplateService = fileTemplateService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        _messengerService.Register<MonitoredResourceChangedMessage>(this, OnMonitoredResourceChangedMessage);
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);
    }

    private void OnMonitoredResourceChangedMessage(object recipient, MonitoredResourceChangedMessage message)
    {
        if (message.Resource == FileResource)
        {
            // Skip reload if we're currently saving - this is our own file change
            if (IsSavingFile)
            {
                return;
            }

            if (IsFileChangedExternally())
            {
                RaiseReloadRequested();
            }
        }
    }

    private void OnDocumentSaveCompletedMessage(object recipient, DocumentSaveCompletedMessage message)
    {
        if (message.DocumentResource == FileResource)
        {
            UpdateFileTrackingInfo();
        }
    }

    public async Task<Result> LoadContent()
    {
        try
        {
            UpdateFileTrackingInfo();

            await Task.CompletedTask;

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when loading document from file: {FilePath}")
                .WithException(ex);
        }
    }

    public async Task<string> LoadNoteContent()
    {
        if (!File.Exists(FilePath))
        {
            var emptyContent = _fileTemplateService.GetNewFileContent(FilePath);
            return Encoding.UTF8.GetString(emptyContent);
        }

        var content = await File.ReadAllTextAsync(FilePath);
        if (string.IsNullOrEmpty(content))
        {
            var emptyContent = _fileTemplateService.GetNewFileContent(FilePath);
            return Encoding.UTF8.GetString(emptyContent);
        }

        return content;
    }

    public async Task<Result> SaveNoteToFile(string jsonContent)
    {
        try
        {
            // Set flag before writing to suppress file watcher reload requests
            IsSavingFile = true;

            await File.WriteAllTextAsync(FilePath, jsonContent);

            // Update file tracking info immediately after writing
            UpdateFileTrackingInfo();

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save note file: '{FilePath}'")
                .WithException(ex);
        }
        finally
        {
            // Clear the flag after save completes (success or failure)
            IsSavingFile = false;
        }
    }

    /// <summary>
    /// Called after a successful save to reset the save state flags.
    /// </summary>
    public void OnSaveCompleted()
    {
        HasUnsavedChanges = false;
        SaveTimer = 0;
    }

    /// <summary>
    /// Gets the base path (folder) of the current document for resolving relative paths.
    /// Returns an empty string if the document is at the project root.
    /// </summary>
    public string GetDocumentBasePath()
    {
        var fileResourcePath = FileResource.ToString();
        var directoryName = Path.GetDirectoryName(fileResourcePath);
        return directoryName?.Replace('\\', '/') ?? "";
    }

    /// <summary>
    /// Converts an absolute resource key to a path relative to the current document.
    /// </summary>
    public string GetRelativePathFromResourceKey(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey))
        {
            return string.Empty;
        }

        var documentBasePath = GetDocumentBasePath();
        if (string.IsNullOrEmpty(documentBasePath))
        {
            return resourceKey;
        }

        var documentSegments = documentBasePath.Split('/');
        var targetSegments = resourceKey.Split('/');

        // Find common prefix length
        var commonLength = 0;
        var minLength = Math.Min(documentSegments.Length, targetSegments.Length);
        for (var i = 0; i < minLength; i++)
        {
            if (documentSegments[i] == targetSegments[i])
            {
                commonLength++;
            }
            else
            {
                break;
            }
        }

        // Build relative path: go up for remaining document segments, then down to target
        var upCount = documentSegments.Length - commonLength;
        var relativeParts = new List<string>();

        for (var i = 0; i < upCount; i++)
        {
            relativeParts.Add("..");
        }

        for (var i = commonLength; i < targetSegments.Length; i++)
        {
            relativeParts.Add(targetSegments[i]);
        }

        return string.Join("/", relativeParts);
    }

    /// <summary>
    /// Resolves a path to an absolute resource key.
    /// Paths starting with '/' are resolved from the project root.
    /// All other paths are resolved relative to the current document's folder.
    /// </summary>
    public Result<ResourceKey> ResolveResourcePath(string path)
    {
        string fullPath;

        if (path.StartsWith('/'))
        {
            fullPath = path.Substring(1);
        }
        else
        {
            var documentBasePath = GetDocumentBasePath();
            fullPath = string.IsNullOrEmpty(documentBasePath)
                ? path
                : $"{documentBasePath}/{path}";
        }

        var normalizedPath = NormalizeResourcePath(fullPath);
        var resourceKey = new ResourceKey(normalizedPath);
        var result = _resourceRegistry.NormalizeResourceKey(resourceKey);

        if (result.IsSuccess)
        {
            return Result<ResourceKey>.Ok(result.Value);
        }

        return Result<ResourceKey>.Fail($"Could not resolve resource path: {path}");
    }

    /// <summary>
    /// Determines the action to take for a clicked link.
    /// Returns the resolved resource key for internal links, or ResourceKey.Empty for external URLs.
    /// </summary>
    public Result<ResourceKey> ResolveLinkTarget(string href)
    {
        if (string.IsNullOrEmpty(href))
        {
            return Result<ResourceKey>.Ok(ResourceKey.Empty);
        }

        // External URLs return empty to indicate browser handling
        if (Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            return Result<ResourceKey>.Ok(ResourceKey.Empty);
        }

        // Internal link - resolve to resource key
        var resolveResult = ResolveResourcePath(href);
        if (resolveResult.IsFailure)
        {
            return Result<ResourceKey>.Fail(resolveResult.Error);
        }

        return Result<ResourceKey>.Ok(resolveResult.Value);
    }

    /// <summary>
    /// Normalizes a path by resolving '..' and '.' segments.
    /// </summary>
    private static string NormalizeResourcePath(string path)
    {
        var segments = path.Split('/');
        var stack = new Stack<string>();
        foreach (var segment in segments)
        {
            if (segment == ".." && stack.Count > 0)
            {
                stack.Pop();
            }
            else if (segment != "." && !string.IsNullOrEmpty(segment))
            {
                stack.Push(segment);
            }
        }
        return string.Join("/", stack.Reverse());
    }

    public override void Cleanup()
    {
        _messengerService.UnregisterAll(this);
    }
}
