using System.Text;
using Celbridge.Packages;
using Celbridge.Workspace;

namespace Celbridge.Documents.ViewModels;

/// <summary>
/// View model for contribution document editors.
/// Provides text file I/O, file-change monitoring, path resolution, and template content
/// for custom contribution editors.
/// </summary>
public partial class ContributionDocumentViewModel : DocumentViewModel
{
    private readonly IResourceRegistry _resourceRegistry;

    /// <summary>
    /// The document contribution this view model serves.
    /// Set by the view after construction.
    /// </summary>
    public CustomDocumentContribution? Contribution { get; set; }

    public ContributionDocumentViewModel(IWorkspaceWrapper workspaceWrapper)
    {
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        EnableFileChangeMonitoring();
    }

    /// <summary>
    /// Whether this contribution handles binary file content.
    /// </summary>
    private bool IsBinary => Contribution?.Binary ?? false;

    /// <summary>
    /// Loads content from the file.
    /// For binary documents, returns base64-encoded content.
    /// For text documents, returns the raw text.
    /// Returns template content for empty or missing files when the manifest declares a default template.
    /// Returns empty string if no template is available.
    /// </summary>
    public async Task<string> LoadTextContentAsync()
    {
        if (!File.Exists(FilePath))
        {
            return GetDefaultTemplateContent();
        }

        if (IsBinary)
        {
            var bytes = await File.ReadAllBytesAsync(FilePath);
            if (bytes.Length == 0)
            {
                return GetDefaultTemplateContent();
            }

            UpdateFileTrackingInfo();
            return Convert.ToBase64String(bytes);
        }

        var content = await File.ReadAllTextAsync(FilePath);

        if (string.IsNullOrEmpty(content))
        {
            return GetDefaultTemplateContent();
        }

        UpdateFileTrackingInfo();
        return content;
    }

    /// <summary>
    /// Saves content to the file.
    /// For binary documents, decodes base64 content and writes raw bytes.
    /// For text documents, writes the content as text.
    /// </summary>
    public async Task<Result> SaveTextContentAsync(string content)
    {
        if (IsBinary)
        {
            return await SaveBinaryToFileAsync(content);
        }

        return await SaveTextToFileAsync(content);
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
        return FileResource.GetParent().ToString();
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
        if (!ResourceKey.TryCreate(normalizedPath, out var resourceKey))
        {
            return Result<ResourceKey>.Fail($"Invalid resource key derived from path: {normalizedPath}");
        }
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
    /// Reads the default template content from the manifest's template file.
    /// Returns empty string if no default template is declared or the file cannot be read.
    /// </summary>
    private string GetDefaultTemplateContent()
    {
        if (Contribution is null)
        {
            return string.Empty;
        }

        var defaultTemplate = Contribution.Templates
            .FirstOrDefault(t => t.Default);

        if (defaultTemplate is null)
        {
            return string.Empty;
        }

        var templatePath = Path.Combine(Contribution.Package.PackageFolder, defaultTemplate.TemplateFile);
        if (!File.Exists(templatePath))
        {
            return string.Empty;
        }

        try
        {
            return File.ReadAllText(templatePath, Encoding.UTF8);
        }
        catch
        {
            return string.Empty;
        }
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
}
