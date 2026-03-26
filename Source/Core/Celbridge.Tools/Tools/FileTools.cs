using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
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
    /// When offset or limit are specified, returns JSON with content and totalLineCount.
    /// When neither is specified, returns the raw file content as plain text.
    /// </summary>
    /// <param name="resource">Resource key of the file to read.</param>
    /// <param name="offset">Starting line number (1-based). Use 0 to read from the beginning.</param>
    /// <param name="limit">Maximum number of lines to return. Use 0 to read to the end.</param>
    /// <returns>Plain text when reading the whole file, or JSON with fields: content (string), totalLineCount (int) when using offset/limit.</returns>
    [McpServerTool(Name = "file_read", ReadOnly = true)]
    [ToolAlias("file.read")]
    public async partial Task<CallToolResult> Read(string resource, int offset = 0, int limit = 0)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourcePath = resourceRegistry.GetResourcePath(resource);

        if (!File.Exists(resourcePath))
        {
            return ErrorResult($"File not found: '{resource}'");
        }

        if (offset == 0 && limit == 0)
        {
            var text = await File.ReadAllTextAsync(resourcePath);
            return SuccessResult(text);
        }

        var lines = await File.ReadAllLinesAsync(resourcePath);
        var totalLineCount = lines.Length;
        var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
        var count = limit > 0 ? limit : lines.Length - startIndex;
        count = Math.Min(count, lines.Length - startIndex);

        if (startIndex >= lines.Length)
        {
            return SuccessResult(JsonSerializer.Serialize(new
            {
                content = string.Empty,
                totalLineCount
            }));
        }

        var selectedLines = lines.Skip(startIndex).Take(count);
        var content = string.Join(Environment.NewLine, selectedLines);

        return SuccessResult(JsonSerializer.Serialize(new
        {
            content,
            totalLineCount
        }));
    }

    /// <summary>
    /// Reads a binary file and returns its content as base64 with MIME type.
    /// </summary>
    /// <param name="resource">Resource key of the file to read.</param>
    /// <returns>JSON object with fields: base64 (string), mimeType (string), size (int).</returns>
    [McpServerTool(Name = "file_read_binary", ReadOnly = true)]
    [ToolAlias("file.read_binary")]
    public async partial Task<CallToolResult> ReadBinary(string resource)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourcePath = resourceRegistry.GetResourcePath(resource);

        if (!File.Exists(resourcePath))
        {
            return ErrorResult($"File not found: '{resource}'");
        }

        var bytes = await File.ReadAllBytesAsync(resourcePath);
        var base64 = Convert.ToBase64String(bytes);
        var extension = Path.GetExtension(resourcePath).ToLowerInvariant();
        var mimeType = GetMimeType(extension);

        return SuccessResult(JsonSerializer.Serialize(new
        {
            base64,
            mimeType,
            size = bytes.Length
        }));
    }

    /// <summary>
    /// Gets metadata about a resource including type, size, modified date, and extension.
    /// </summary>
    /// <param name="resource">Resource key of the resource to inspect.</param>
    /// <returns>JSON object with fields: type (string: "file" or "folder"), size (long, files only), modified (string, ISO 8601), extension (string, files only).</returns>
    [McpServerTool(Name = "file_get_info", ReadOnly = true)]
    [ToolAlias("file.get_info")]
    public partial CallToolResult GetInfo(string resource)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourcePath = resourceRegistry.GetResourcePath(resource);

        if (File.Exists(resourcePath))
        {
            var fileInfo = new FileInfo(resourcePath);
            return SuccessResult(JsonSerializer.Serialize(new
            {
                type = "file",
                size = fileInfo.Length,
                modified = fileInfo.LastWriteTimeUtc.ToString("o"),
                extension = fileInfo.Extension
            }));
        }

        if (Directory.Exists(resourcePath))
        {
            var directoryInfo = new DirectoryInfo(resourcePath);
            return SuccessResult(JsonSerializer.Serialize(new
            {
                type = "folder",
                modified = directoryInfo.LastWriteTimeUtc.ToString("o")
            }));
        }

        return ErrorResult($"Resource not found: '{resource}'");
    }

    /// <summary>
    /// Lists the immediate children of a folder with their type and size.
    /// Optionally filters children by a glob pattern.
    /// </summary>
    /// <param name="resource">Resource key of the folder to list.</param>
    /// <param name="glob">Optional glob pattern to filter children by name (e.g. "*.py", "readme*"). When empty, all children are returned.</param>
    /// <returns>JSON array of objects with fields: name (string), type (string: "file" or "folder"), size (long, files only).</returns>
    [McpServerTool(Name = "file_list_contents", ReadOnly = true)]
    [ToolAlias("file.list_contents")]
    public partial CallToolResult ListContents(string resource, string glob = "")
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            return ErrorResult($"Resource not found: '{resource}'");
        }

        if (getResult.Value is not IFolderResource folderResource)
        {
            return ErrorResult($"Resource is not a folder: '{resource}'");
        }

        Regex? globRegex = null;
        if (!string.IsNullOrEmpty(glob))
        {
            var regexPattern = GlobToRegex(glob);
            globRegex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        }

        var items = new List<object>();
        foreach (var child in folderResource.Children)
        {
            if (globRegex is not null && !globRegex.IsMatch(child.Name))
            {
                continue;
            }

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

        return SuccessResult(JsonSerializer.Serialize(items));
    }

    /// <summary>
    /// Returns a recursive folder tree as JSON with configurable depth.
    /// </summary>
    /// <param name="resource">Resource key of the root folder.</param>
    /// <param name="depth">Maximum depth to traverse. Default is 3.</param>
    /// <returns>JSON tree where each node has: name (string), type (string), and children (array, folders only).</returns>
    [McpServerTool(Name = "file_get_tree", ReadOnly = true)]
    [ToolAlias("file.get_tree")]
    public partial CallToolResult GetTree(string resource, int depth = 3)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            return ErrorResult($"Resource not found: '{resource}'");
        }

        if (getResult.Value is not IFolderResource folderResource)
        {
            return ErrorResult($"Resource is not a folder: '{resource}'");
        }

        var tree = BuildTree(folderResource, resourceRegistry, depth);
        return SuccessResult(JsonSerializer.Serialize(tree));
    }

    /// <summary>
    /// Searches for resources by name using a glob pattern.
    /// </summary>
    /// <param name="pattern">Glob pattern to match resource names (e.g. "*.py", "readme*").</param>
    /// <returns>JSON array of matching resource keys.</returns>
    [McpServerTool(Name = "file_search", ReadOnly = true)]
    [ToolAlias("file.search")]
    public partial CallToolResult Search(string pattern)
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

        return SuccessResult(JsonSerializer.Serialize(matches));
    }

    /// <summary>
    /// Searches file contents by text, returning matches with line numbers and optional context lines.
    /// </summary>
    /// <param name="searchTerm">The text to search for in file contents.</param>
    /// <param name="matchCase">If true, the search is case-sensitive.</param>
    /// <param name="wholeWord">If true, only match whole words.</param>
    /// <param name="maxResults">Maximum number of matches to return. Default is 100.</param>
    /// <param name="contextLines">Number of lines to include before and after each match (like grep -C). Default is 0.</param>
    /// <returns>JSON object with fields: totalMatches (int), totalFiles (int), files (array of objects with resource, fileName, matches array with lineNumber, lineText, matchStart, matchLength, and contextBefore/contextAfter arrays when contextLines > 0).</returns>
    [McpServerTool(Name = "file_grep", ReadOnly = true)]
    [ToolAlias("file.grep")]
    public async partial Task<CallToolResult> Grep(string searchTerm, bool matchCase = false, bool wholeWord = false, int maxResults = 100, int contextLines = 0)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var searchService = workspaceWrapper.WorkspaceService.SearchService;
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var results = await searchService.SearchAsync(
            searchTerm,
            matchCase,
            wholeWord,
            maxResults,
            CancellationToken.None);

        // Cache file lines for context extraction to avoid reading the same file multiple times
        var fileLineCache = new Dictionary<string, string[]>();

        var fileResults = new List<object>();
        foreach (var fileResult in results.FileResults)
        {
            var matchList = new List<object>();

            foreach (var match in fileResult.Matches)
            {
                if (contextLines > 0)
                {
                    var resourcePath = resourceRegistry.GetResourcePath(fileResult.Resource);

                    if (!fileLineCache.TryGetValue(resourcePath, out var fileLines))
                    {
                        fileLines = File.Exists(resourcePath) ? await File.ReadAllLinesAsync(resourcePath) : Array.Empty<string>();
                        fileLineCache[resourcePath] = fileLines;
                    }

                    var matchLineIndex = match.LineNumber - 1;
                    var contextBeforeStart = Math.Max(0, matchLineIndex - contextLines);
                    var contextAfterEnd = Math.Min(fileLines.Length - 1, matchLineIndex + contextLines);

                    var contextBefore = new List<string>();
                    for (int i = contextBeforeStart; i < matchLineIndex; i++)
                    {
                        contextBefore.Add(fileLines[i]);
                    }

                    var contextAfter = new List<string>();
                    for (int i = matchLineIndex + 1; i <= contextAfterEnd; i++)
                    {
                        contextAfter.Add(fileLines[i]);
                    }

                    matchList.Add(new
                    {
                        lineNumber = match.LineNumber,
                        lineText = match.LineText,
                        matchStart = match.MatchStart,
                        matchLength = match.MatchLength,
                        contextBefore,
                        contextAfter
                    });
                }
                else
                {
                    matchList.Add(new
                    {
                        lineNumber = match.LineNumber,
                        lineText = match.LineText,
                        matchStart = match.MatchStart,
                        matchLength = match.MatchLength
                    });
                }
            }

            fileResults.Add(new
            {
                resource = fileResult.Resource.ToString(),
                fileName = fileResult.FileName,
                matches = matchList
            });
        }

        return SuccessResult(JsonSerializer.Serialize(new
        {
            totalMatches = results.TotalMatches,
            totalFiles = results.TotalFiles,
            files = fileResults
        }));
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
