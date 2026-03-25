using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for read-only file and folder queries.
/// </summary>
[McpServerToolType]
public partial class FileTools : AgentToolBase
{
    public FileTools(IApplicationServiceProvider services) : base(services) { }

    /// <summary>
    /// Reads the text content of a file. Supports optional line range via offset and limit.
    /// </summary>
    /// <param name="resource">Resource key of the file to read.</param>
    /// <param name="offset">Starting line number (1-based). Use 0 to read from the beginning.</param>
    /// <param name="limit">Maximum number of lines to return. Use 0 to read to the end.</param>
    /// <returns>The text content of the file, or the specified line range.</returns>
    [McpServerTool(Name = "file_read", ReadOnly = true)]
    [ToolAlias("file.read")]
    public async partial Task<string> Read(string resource, int offset = 0, int limit = 0)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourcePath = resourceRegistry.GetResourcePath(resource);

        if (!File.Exists(resourcePath))
        {
            throw new FileNotFoundException($"File not found: '{resource}'");
        }

        if (offset == 0 && limit == 0)
        {
            return await File.ReadAllTextAsync(resourcePath);
        }

        var lines = await File.ReadAllLinesAsync(resourcePath);
        var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
        var count = limit > 0 ? limit : lines.Length - startIndex;
        count = Math.Min(count, lines.Length - startIndex);

        if (startIndex >= lines.Length)
        {
            return string.Empty;
        }

        var selectedLines = lines.Skip(startIndex).Take(count);
        return string.Join(Environment.NewLine, selectedLines);
    }

    /// <summary>
    /// Reads a binary file and returns its content as base64 with MIME type.
    /// </summary>
    /// <param name="resource">Resource key of the file to read.</param>
    /// <returns>JSON object with fields: base64 (string), mimeType (string), size (int).</returns>
    [McpServerTool(Name = "file_read_binary", ReadOnly = true)]
    [ToolAlias("file.read_binary")]
    public async partial Task<string> ReadBinary(string resource)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourcePath = resourceRegistry.GetResourcePath(resource);

        if (!File.Exists(resourcePath))
        {
            throw new FileNotFoundException($"File not found: '{resource}'");
        }

        var bytes = await File.ReadAllBytesAsync(resourcePath);
        var base64 = Convert.ToBase64String(bytes);
        var extension = Path.GetExtension(resourcePath).ToLowerInvariant();
        var mimeType = GetMimeType(extension);

        return JsonSerializer.Serialize(new
        {
            base64,
            mimeType,
            size = bytes.Length
        });
    }

    /// <summary>
    /// Gets metadata about a resource including type, size, modified date, and extension.
    /// </summary>
    /// <param name="resource">Resource key of the resource to inspect.</param>
    /// <returns>JSON object with fields: type (string: "file" or "folder"), size (long, files only), modified (string, ISO 8601), extension (string, files only).</returns>
    [McpServerTool(Name = "file_get_info", ReadOnly = true)]
    [ToolAlias("file.get_info")]
    public partial string GetInfo(string resource)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourcePath = resourceRegistry.GetResourcePath(resource);

        if (File.Exists(resourcePath))
        {
            var fileInfo = new FileInfo(resourcePath);
            return JsonSerializer.Serialize(new
            {
                type = "file",
                size = fileInfo.Length,
                modified = fileInfo.LastWriteTimeUtc.ToString("o"),
                extension = fileInfo.Extension
            });
        }

        if (Directory.Exists(resourcePath))
        {
            var directoryInfo = new DirectoryInfo(resourcePath);
            return JsonSerializer.Serialize(new
            {
                type = "folder",
                modified = directoryInfo.LastWriteTimeUtc.ToString("o")
            });
        }

        throw new FileNotFoundException($"Resource not found: '{resource}'");
    }

    /// <summary>
    /// Lists the immediate children of a folder with their type and size.
    /// </summary>
    /// <param name="resource">Resource key of the folder to list.</param>
    /// <returns>JSON array of objects with fields: name (string), type (string: "file" or "folder"), size (long, files only).</returns>
    [McpServerTool(Name = "file_list_contents", ReadOnly = true)]
    [ToolAlias("file.list_contents")]
    public partial string ListContents(string resource)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            throw new FileNotFoundException($"Resource not found: '{resource}'");
        }

        if (getResult.Value is not IFolderResource folderResource)
        {
            throw new InvalidOperationException($"Resource is not a folder: '{resource}'");
        }

        var items = new List<object>();
        foreach (var child in folderResource.Children)
        {
            var childKey = resourceRegistry.GetResourceKey(child);
            var childPath = resourceRegistry.GetResourcePath(childKey);

            if (child is IFolderResource)
            {
                items.Add(new { name = child.Name, type = "folder" });
            }
            else
            {
                var fileInfo = new FileInfo(childPath);
                items.Add(new { name = child.Name, type = "file", size = fileInfo.Length });
            }
        }

        return JsonSerializer.Serialize(items);
    }

    /// <summary>
    /// Returns a recursive folder tree as JSON with configurable depth.
    /// </summary>
    /// <param name="resource">Resource key of the root folder.</param>
    /// <param name="depth">Maximum depth to traverse. Default is 3.</param>
    /// <returns>JSON tree where each node has: name (string), type (string), and children (array, folders only).</returns>
    [McpServerTool(Name = "file_get_tree", ReadOnly = true)]
    [ToolAlias("file.get_tree")]
    public partial string GetTree(string resource, int depth = 3)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            throw new FileNotFoundException($"Resource not found: '{resource}'");
        }

        if (getResult.Value is not IFolderResource folderResource)
        {
            throw new InvalidOperationException($"Resource is not a folder: '{resource}'");
        }

        var tree = BuildTree(folderResource, resourceRegistry, depth);
        return JsonSerializer.Serialize(tree);
    }

    /// <summary>
    /// Searches for resources by name using a glob pattern.
    /// </summary>
    /// <param name="pattern">Glob pattern to match resource names (e.g. "*.py", "readme*").</param>
    /// <returns>JSON array of matching resource keys.</returns>
    [McpServerTool(Name = "file_search", ReadOnly = true)]
    [ToolAlias("file.search")]
    public partial string Search(string pattern)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var allResources = resourceRegistry.GetAllFileResources();
        var regexPattern = GlobToRegex(pattern);
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        var matches = allResources
            .Where(r => regex.IsMatch(r.Resource.ResourceName))
            .Select(r => r.Resource.ToString())
            .ToList();

        return JsonSerializer.Serialize(matches);
    }

    /// <summary>
    /// Searches file contents by text, returning matches with line numbers.
    /// </summary>
    /// <param name="searchTerm">The text to search for in file contents.</param>
    /// <param name="matchCase">If true, the search is case-sensitive.</param>
    /// <param name="wholeWord">If true, only match whole words.</param>
    /// <param name="maxResults">Maximum number of matches to return. Default is 100.</param>
    /// <returns>JSON object with fields: totalMatches (int), totalFiles (int), files (array of objects with resource, fileName, matches array with lineNumber, lineText, matchStart, matchLength).</returns>
    [McpServerTool(Name = "file_grep", ReadOnly = true)]
    [ToolAlias("file.grep")]
    public async partial Task<string> Grep(string searchTerm, bool matchCase = false, bool wholeWord = false, int maxResults = 100)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var searchService = workspaceWrapper.WorkspaceService.SearchService;

        var results = await searchService.SearchAsync(
            searchTerm,
            matchCase,
            wholeWord,
            maxResults,
            CancellationToken.None);

        var fileResults = results.FileResults.Select(fileResult => new
        {
            resource = fileResult.Resource.ToString(),
            fileName = fileResult.FileName,
            matches = fileResult.Matches.Select(match => new
            {
                lineNumber = match.LineNumber,
                lineText = match.LineText,
                matchStart = match.MatchStart,
                matchLength = match.MatchLength
            }).ToList()
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            totalMatches = results.TotalMatches,
            totalFiles = results.TotalFiles,
            files = fileResults
        });
    }

    private static object BuildTree(IFolderResource folder, IResourceRegistry registry, int remainingDepth)
    {
        var children = new List<object>();

        if (remainingDepth > 0)
        {
            foreach (var child in folder.Children)
            {
                if (child is IFolderResource childFolder)
                {
                    children.Add(BuildTree(childFolder, registry, remainingDepth - 1));
                }
                else
                {
                    children.Add(new { name = child.Name, type = "file" });
                }
            }
        }

        return new
        {
            name = folder.Name,
            type = "folder",
            children
        };
    }

    private static string GetMimeType(string extension)
    {
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".gz" => "application/gzip",
            ".tar" => "application/x-tar",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            _ => "application/octet-stream"
        };
    }

    private static string GlobToRegex(string glob)
    {
        var regexPattern = Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^{regexPattern}$";
    }
}
