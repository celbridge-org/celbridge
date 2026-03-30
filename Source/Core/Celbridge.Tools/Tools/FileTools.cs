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
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FileTools(IApplicationServiceProvider services) : base(services) { }

    /// <summary>
    /// Reads the text content of a file. Always returns JSON with content and totalLineCount.
    /// Supports optional line range via offset and limit.
    /// For large files, use file_get_info first to check line count and size before reading.
    /// </summary>
    /// <param name="resource">Resource key of the file to read.</param>
    /// <param name="offset">Starting line number (1-based). Use 0 to read from the beginning.</param>
    /// <param name="limit">Maximum number of lines to return. Use 0 to read to the end.</param>
    /// <param name="lineNumbers">When true, prefix each line in content with its 1-based line number (e.g. "1: first line"). Line numbers reflect actual positions in the file, even when using offset.</param>
    /// <returns>JSON with fields: content (string), totalLineCount (int).</returns>
    [McpServerTool(Name = "file_read", ReadOnly = true)]
    [ToolAlias("file.read")]
    public async partial Task<CallToolResult> Read(string resource, int offset = 0, int limit = 0, bool lineNumbers = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve path for resource: '{resource}'");
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return ErrorResult($"File not found: '{resource}'");
        }

        if (offset == 0 && limit == 0)
        {
            var text = await File.ReadAllTextAsync(resourcePath);
            var lineCount = FileReadHelper.CountLines(text);

            if (lineNumbers)
            {
                var splitLines = text.Split('\n');
                text = FileReadHelper.AddLineNumbers(splitLines, 1);
            }

            var wholeFileResult = new FileReadResult(text, lineCount);
            return SuccessResult(SerializeJson(wholeFileResult));
        }

        var lines = await File.ReadAllLinesAsync(resourcePath);
        var totalLineCount = lines.Length;
        var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
        var count = limit > 0 ? limit : lines.Length - startIndex;
        count = Math.Min(count, lines.Length - startIndex);

        if (startIndex >= lines.Length)
        {
            var emptyResult = new FileReadResult(string.Empty, totalLineCount);
            return SuccessResult(SerializeJson(emptyResult));
        }

        var selectedLines = lines.Skip(startIndex).Take(count).ToArray();
        string content;

        if (lineNumbers)
        {
            var firstLineNumber = startIndex + 1;
            content = FileReadHelper.AddLineNumbers(selectedLines, firstLineNumber);
        }
        else
        {
            content = string.Join(Environment.NewLine, selectedLines);
        }

        var readResult = new FileReadResult(content, totalLineCount);
        return SuccessResult(SerializeJson(readResult));
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
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve path for resource: '{resource}'");
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return ErrorResult($"File not found: '{resource}'");
        }

        var bytes = await File.ReadAllBytesAsync(resourcePath);
        var base64 = Convert.ToBase64String(bytes);
        var extension = Path.GetExtension(resourcePath).ToLowerInvariant();
        var mimeType = GetMimeType(extension);

        var result = new FileReadBinaryResult(base64, mimeType, bytes.Length);
        return SuccessResult(SerializeJson(result));
    }

    /// <summary>
    /// Gets metadata about a resource including type, size, modified date, extension, and text/binary indicator.
    /// For text files, also returns the line count.
    /// </summary>
    /// <param name="resource">Resource key of the resource to inspect.</param>
    /// <returns>JSON object with fields: type (string: "file" or "folder"), size (long, files only), modified (string, ISO 8601), extension (string, files only), isText (bool, files only), lineCount (int, text files only).</returns>
    [McpServerTool(Name = "file_get_info", ReadOnly = true)]
    [ToolAlias("file.get_info")]
    public partial CallToolResult GetInfo(string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve path for resource: '{resource}'");
        }
        var resourcePath = resolveResult.Value;

        if (File.Exists(resourcePath))
        {
            var fileInfo = new FileInfo(resourcePath);
            var textBinarySniffer = GetRequiredService<ITextBinarySniffer>();
            var isText = IsTextFile(textBinarySniffer, resourcePath);
            int? lineCount = null;

            if (isText)
            {
                lineCount = File.ReadAllLines(resourcePath).Length;
            }

            var result = new FileInfoResult(
                "file",
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.ToString("o"),
                fileInfo.Extension,
                isText,
                lineCount);
            return SuccessResult(SerializeJson(result));
        }

        if (Directory.Exists(resourcePath))
        {
            var directoryInfo = new DirectoryInfo(resourcePath);
            var result = new FolderInfoResult("folder", directoryInfo.LastWriteTimeUtc.ToString("o"));
            return SuccessResult(SerializeJson(result));
        }

        return ErrorResult($"Resource not found: '{resource}'");
    }

    /// <summary>
    /// Lists the immediate children of a folder with their type, size, and modification date.
    /// Optionally filters children by a glob pattern.
    /// </summary>
    /// <param name="resource">Resource key of the folder to list.</param>
    /// <param name="glob">Optional glob pattern to filter children by name (e.g. "*.py", "readme*"). When empty, all children are returned.</param>
    /// <returns>JSON array of objects with fields: name (string), type (string: "file" or "folder"), size (long, files only), modified (string, ISO 8601).</returns>
    [McpServerTool(Name = "file_list_contents", ReadOnly = true)]
    [ToolAlias("file.list_contents")]
    public partial CallToolResult ListContents(string resource, string glob = "")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resourceKey);
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
            var regexPattern = GlobHelper.GlobToRegex(glob);
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

            if (child is IFolderResource)
            {
                var resolveChildResult = resourceRegistry.ResolveResourcePath(childKey);
                if (resolveChildResult.IsFailure)
                {
                    continue;
                }
                var directoryInfo = new DirectoryInfo(resolveChildResult.Value);
                var folderItem = new ListContentsFolderItem(
                    child.Name,
                    "folder",
                    directoryInfo.LastWriteTimeUtc.ToString("o"));
                items.Add(folderItem);
            }
            else
            {
                var resolveChildResult = resourceRegistry.ResolveResourcePath(childKey);
                if (resolveChildResult.IsFailure)
                {
                    continue;
                }
                var fileInfo = new FileInfo(resolveChildResult.Value);
                var fileItem = new ListContentsFileItem(
                    child.Name,
                    "file",
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.ToString("o"));
                items.Add(fileItem);
            }
        }

        return SuccessResult(SerializeJson(items));
    }

    /// <summary>
    /// Returns a recursive folder tree as JSON with configurable depth.
    /// Supports optional glob filtering to show only files matching a pattern, and type filtering to show only files or folders.
    /// Folder nodes at the depth limit that have children include a truncated flag.
    /// </summary>
    /// <param name="resource">Resource key of the root folder.</param>
    /// <param name="depth">Maximum depth to traverse. Default is 3.</param>
    /// <param name="glob">Optional glob pattern to filter files by name (e.g. "*.cs", "*.py"). Folders are always included if they have matching descendants. When empty, all files are shown.</param>
    /// <param name="type">Optional type filter: "file" to show only files, "folder" to show only folders. When empty, both are shown.</param>
    /// <returns>JSON tree where each node has: name (string), type (string), children (array, folders only), and truncated (bool, present on folder nodes at depth limit that have children).</returns>
    [McpServerTool(Name = "file_get_tree", ReadOnly = true)]
    [ToolAlias("file.get_tree")]
    public partial CallToolResult GetTree(string resource, int depth = 3, string glob = "", string type = "")
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(resourceKey);
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
            var regexPattern = GlobHelper.GlobToRegex(glob);
            globRegex = new Regex(regexPattern, RegexOptions.IgnoreCase);
        }

        var tree = FileToolHelpers.BuildTree(folderResource, depth, globRegex, type)
            ?? new TreeFolderNode(folderResource.Name, "folder", new List<object>());
        return SuccessResult(SerializeJson(tree));
    }

    /// <summary>
    /// Searches for resources by name using a glob pattern matched against the full resource path.
    /// Supports ** for matching across path separators (e.g. "**/Commands/*.cs").
    /// </summary>
    /// <param name="pattern">Glob pattern to match resource paths (e.g. "*.py", "**/Commands/*.cs", "Services/**/I*.cs").</param>
    /// <param name="includeMetadata">When true, returns objects with resource, size, and modified fields instead of plain resource key strings.</param>
    /// <param name="type">Resource type to search: "file" (default) or "folder".</param>
    /// <returns>When includeMetadata is false: JSON array of matching resource key strings. When true: JSON array of objects with resource (string), size (long), modified (string, ISO 8601).</returns>
    [McpServerTool(Name = "file_search", ReadOnly = true)]
    [ToolAlias("file.search")]
    public partial CallToolResult Search(string pattern, bool includeMetadata = false, string type = "")
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var regexPattern = GlobHelper.PathGlobToRegex(pattern);
        var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

        var isFolderSearch = string.Equals(type, "folder", StringComparison.OrdinalIgnoreCase);

        if (isFolderSearch)
        {
            var folderKeys = new List<ResourceKey>();
            CollectFolderResources(resourceRegistry.RootFolder, resourceRegistry, folderKeys);

            var matchingFolders = folderKeys
                .Where(key => regex.IsMatch(key.ToString()))
                .ToList();

            if (includeMetadata)
            {
                var results = new List<SearchResultWithMetadata>();
                foreach (var folderKey in matchingFolders)
                {
                    var resolvePathResult = resourceRegistry.ResolveResourcePath(folderKey);
                    if (resolvePathResult.IsFailure)
                    {
                        continue;
                    }
                    var directoryInfo = new DirectoryInfo(resolvePathResult.Value);
                    results.Add(new SearchResultWithMetadata(
                        folderKey.ToString(),
                        0,
                        directoryInfo.LastWriteTimeUtc.ToString("o")));
                }
                return SuccessResult(SerializeJson(results));
            }

            var folderStrings = matchingFolders.Select(key => key.ToString()).ToList();
            return SuccessResult(SerializeJson(folderStrings));
        }

        var allResources = resourceRegistry.GetAllFileResources();

        var matches = allResources
            .Where(r => regex.IsMatch(r.Resource.ToString()))
            .ToList();

        if (includeMetadata)
        {
            var results = new List<SearchResultWithMetadata>();
            foreach (var match in matches)
            {
                var fileInfo = new FileInfo(match.Path);
                results.Add(new SearchResultWithMetadata(
                    match.Resource.ToString(),
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc.ToString("o")));
            }
            return SuccessResult(SerializeJson(results));
        }

        var resourceStrings = matches.Select(r => r.Resource.ToString()).ToList();
        return SuccessResult(SerializeJson(resourceStrings));
    }

    /// <summary>
    /// Searches file contents by text or regex, returning matches with line numbers and optional context lines.
    /// Use the files parameter to search specific files directly, or use resource/include/exclude to scope by folder or glob.
    /// </summary>
    /// <param name="searchTerm">The text or regular expression to search for in file contents.</param>
    /// <param name="useRegex">If true, searchTerm is interpreted as a .NET regular expression.</param>
    /// <param name="matchCase">If true, the search is case-sensitive. Ignored when useRegex is true (embed (?-i) in the pattern instead).</param>
    /// <param name="wholeWord">If true, only match whole words. Ignored when useRegex is true (use \b in the pattern instead).</param>
    /// <param name="include">Comma-separated glob patterns to restrict which files are searched (e.g. "*.cs,*.xaml"). When empty, all text files are searched.</param>
    /// <param name="exclude">Comma-separated glob patterns to exclude files from the search (e.g. "*.generated.cs,*.g.cs"). Excluded files are skipped even if they match include.</param>
    /// <param name="resource">Resource key of a folder to scope the search to. Only files within this folder are searched. When empty, all project files are searched.</param>
    /// <param name="maxResults">Maximum number of matches to return. Default is 100.</param>
    /// <param name="contextLines">Number of lines to include before and after each match (like grep -C). Default is 0.</param>
    /// <param name="files">JSON array of resource key strings to search (e.g. ["src/foo.cs","src/bar.cs"]). When provided, only these files are searched and resource/include/exclude are ignored.</param>
    /// <param name="includeContent">When true, each file entry in the result includes the full file content alongside the matches. Useful for collapsing a grep+read workflow into a single call.</param>
    /// <returns>JSON object with fields: totalMatches (int), totalFiles (int), truncated (bool), files (array of objects with resource, fileName, matches array with lineNumber, lineText, matchStart, matchLength, contextBefore/contextAfter arrays when contextLines > 0, and content (string) when includeContent is true).</returns>
    [McpServerTool(Name = "file_grep", ReadOnly = true)]
    [ToolAlias("file.grep")]
    public async partial Task<CallToolResult> Grep(string searchTerm, bool useRegex = false, bool matchCase = false, bool wholeWord = false, string include = "", string exclude = "", string resource = "", int maxResults = 100, int contextLines = 0, string files = "", bool includeContent = false)
    {
        if (useRegex)
        {
            try
            {
                _ = new Regex(searchTerm);
            }
            catch (ArgumentException ex)
            {
                return ErrorResult($"Invalid regular expression: {ex.Message}");
            }
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        if (!string.IsNullOrEmpty(files))
        {
            return await GrepTargetedFiles(files, searchTerm, useRegex, matchCase, wholeWord, maxResults, contextLines, includeContent, resourceRegistry);
        }

        var searchService = workspaceWrapper.WorkspaceService.SearchService;

        var results = await searchService.SearchAsync(
            searchTerm,
            matchCase,
            wholeWord,
            maxResults,
            CancellationToken.None,
            useRegex,
            include,
            exclude,
            resource);

        var truncated = results.ReachedMaxResults || results.WasCancelled;

        var fileLineCache = new Dictionary<string, string[]>();

        var fileResults = new List<GrepFileResult>();
        foreach (var fileResult in results.FileResults)
        {
            var matchList = new List<object>();

            foreach (var match in fileResult.Matches)
            {
                if (contextLines > 0)
                {
                    var resolveContextResult = resourceRegistry.ResolveResourcePath(fileResult.Resource);
                    if (resolveContextResult.IsFailure)
                    {
                        matchList.Add(new GrepMatch(match.LineNumber, match.LineText, match.MatchStart, match.MatchLength));
                        continue;
                    }
                    var resourcePath = resolveContextResult.Value;

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

                    matchList.Add(new GrepMatchWithContext(
                        match.LineNumber,
                        match.LineText,
                        match.MatchStart,
                        match.MatchLength,
                        contextBefore,
                        contextAfter));
                }
                else
                {
                    matchList.Add(new GrepMatch(
                        match.LineNumber,
                        match.LineText,
                        match.MatchStart,
                        match.MatchLength));
                }
            }

            string? fileContent = null;
            if (includeContent)
            {
                var resolveContentResult = resourceRegistry.ResolveResourcePath(fileResult.Resource);
                if (resolveContentResult.IsSuccess && File.Exists(resolveContentResult.Value))
                {
                    fileContent = await File.ReadAllTextAsync(resolveContentResult.Value);
                }
            }

            fileResults.Add(new GrepFileResult(
                fileResult.Resource.ToString(),
                fileResult.FileName,
                matchList,
                fileContent));
        }

        var grepResult = new GrepResult(results.TotalMatches, results.TotalFiles, truncated, fileResults);
        return SuccessResult(SerializeJson(grepResult));
    }

    private async Task<CallToolResult> GrepTargetedFiles(string filesJson, string searchTerm, bool useRegex, bool matchCase, bool wholeWord, int maxResults, int contextLines, bool includeContent, IResourceRegistry resourceRegistry)
    {
        List<string>? fileKeyStrings;
        try
        {
            fileKeyStrings = JsonSerializer.Deserialize<List<string>>(filesJson);
        }
        catch (JsonException ex)
        {
            return ErrorResult($"Invalid JSON array for files: {ex.Message}");
        }

        if (fileKeyStrings is null || fileKeyStrings.Count == 0)
        {
            return ErrorResult("No resource keys provided in files parameter.");
        }

        var searchPattern = useRegex ? searchTerm : Regex.Escape(searchTerm);
        if (wholeWord && !useRegex)
        {
            searchPattern = $@"\b{searchPattern}\b";
        }

        var regexOptions = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        var searchRegex = new Regex(searchPattern, regexOptions);

        var fileResults = new List<GrepFileResult>();
        int totalMatches = 0;
        bool truncated = false;

        foreach (var fileKeyString in fileKeyStrings)
        {
            if (truncated)
            {
                break;
            }

            if (!ResourceKey.TryCreate(fileKeyString, out var fileResourceKey))
            {
                continue;
            }

            var resolveResult = resourceRegistry.ResolveResourcePath(fileResourceKey);
            if (resolveResult.IsFailure)
            {
                continue;
            }
            var filePath = resolveResult.Value;

            if (!File.Exists(filePath))
            {
                continue;
            }

            var fileLines = await File.ReadAllLinesAsync(filePath);
            var matchList = new List<object>();

            for (int lineIdx = 0; lineIdx < fileLines.Length && !truncated; lineIdx++)
            {
                var lineText = fileLines[lineIdx];
                var lineMatches = searchRegex.Matches(lineText);

                foreach (Match regexMatch in lineMatches)
                {
                    var lineNumber = lineIdx + 1;

                    if (contextLines > 0)
                    {
                        var contextBeforeStart = Math.Max(0, lineIdx - contextLines);
                        var contextAfterEnd = Math.Min(fileLines.Length - 1, lineIdx + contextLines);

                        var contextBefore = new List<string>();
                        for (int i = contextBeforeStart; i < lineIdx; i++)
                        {
                            contextBefore.Add(fileLines[i]);
                        }

                        var contextAfter = new List<string>();
                        for (int i = lineIdx + 1; i <= contextAfterEnd; i++)
                        {
                            contextAfter.Add(fileLines[i]);
                        }

                        matchList.Add(new GrepMatchWithContext(
                            lineNumber,
                            lineText,
                            regexMatch.Index,
                            regexMatch.Length,
                            contextBefore,
                            contextAfter));
                    }
                    else
                    {
                        matchList.Add(new GrepMatch(lineNumber, lineText, regexMatch.Index, regexMatch.Length));
                    }

                    totalMatches++;
                    if (totalMatches >= maxResults)
                    {
                        truncated = true;
                        break;
                    }
                }
            }

            if (matchList.Count > 0)
            {
                string? fileContent = null;
                if (includeContent)
                {
                    fileContent = string.Join(Environment.NewLine, fileLines);
                }

                fileResults.Add(new GrepFileResult(
                    fileKeyString,
                    Path.GetFileName(filePath),
                    matchList,
                    fileContent));
            }
        }

        var grepResult = new GrepResult(totalMatches, fileResults.Count, truncated, fileResults);
        return SuccessResult(SerializeJson(grepResult));
    }

    /// <summary>
    /// Reads multiple files in a single call. Each file is read independently; per-entry errors do not fail the whole call.
    /// </summary>
    /// <param name="resources">JSON array of resource key strings to read (e.g. ["src/foo.cs", "src/bar.cs"]).</param>
    /// <param name="offset">Starting line number (1-based) applied to all files. Use 0 to read from the beginning.</param>
    /// <param name="limit">Maximum number of lines to return per file. Use 0 to read to the end.</param>
    /// <returns>JSON object with files array, each entry having: resource (string), content (string), totalLineCount (int), or error (string) on failure.</returns>
    [McpServerTool(Name = "file_read_many", ReadOnly = true)]
    [ToolAlias("file.read_many")]
    public async partial Task<CallToolResult> ReadMany(string resources, int offset = 0, int limit = 0)
    {
        List<string>? resourceKeys;
        try
        {
            resourceKeys = JsonSerializer.Deserialize<List<string>>(resources);
        }
        catch (JsonException ex)
        {
            return ErrorResult($"Invalid JSON array: {ex.Message}");
        }

        if (resourceKeys is null || resourceKeys.Count == 0)
        {
            return ErrorResult("No resource keys provided.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var entries = new List<ReadManyFileEntry>();
        foreach (var resourceString in resourceKeys)
        {
            if (!ResourceKey.TryCreate(resourceString, out var resourceKey))
            {
                entries.Add(new ReadManyFileEntry(resourceString, Error: $"Invalid resource key: '{resourceString}'"));
                continue;
            }

            var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
            if (resolveResult.IsFailure)
            {
                entries.Add(new ReadManyFileEntry(resourceString, Error: $"Failed to resolve path for resource: '{resourceString}'"));
                continue;
            }
            var resourcePath = resolveResult.Value;

            if (!File.Exists(resourcePath))
            {
                entries.Add(new ReadManyFileEntry(resourceString, Error: $"File not found: '{resourceString}'"));
                continue;
            }

            if (offset == 0 && limit == 0)
            {
                var text = await File.ReadAllTextAsync(resourcePath);
                var lineCount = text.Split('\n').Length;
                entries.Add(new ReadManyFileEntry(resourceString, Content: text, TotalLineCount: lineCount));
            }
            else
            {
                var lines = await File.ReadAllLinesAsync(resourcePath);
                var totalLineCount = lines.Length;
                var startIndex = offset > 0 ? Math.Max(0, offset - 1) : 0;
                var count = limit > 0 ? limit : lines.Length - startIndex;
                count = Math.Min(count, lines.Length - startIndex);

                if (startIndex >= lines.Length)
                {
                    entries.Add(new ReadManyFileEntry(resourceString, Content: string.Empty, TotalLineCount: totalLineCount));
                }
                else
                {
                    var selectedLines = lines.Skip(startIndex).Take(count);
                    var content = string.Join(Environment.NewLine, selectedLines);
                    entries.Add(new ReadManyFileEntry(resourceString, Content: content, TotalLineCount: totalLineCount));
                }
            }
        }

        var result = new ReadManyResult(entries);
        return SuccessResult(SerializeJson(result));
    }

    private static void CollectFolderResources(IFolderResource folder, IResourceRegistry registry, List<ResourceKey> folderKeys)
    {
        foreach (var child in folder.Children)
        {
            if (child is IFolderResource childFolder)
            {
                var childKey = registry.GetResourceKey(child);
                folderKeys.Add(childKey);
                CollectFolderResources(childFolder, registry, folderKeys);
            }
        }
    }

    private static bool IsTextFile(ITextBinarySniffer textBinarySniffer, string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (textBinarySniffer.IsBinaryExtension(extension))
        {
            return false;
        }

        var result = textBinarySniffer.IsTextFile(filePath);
        return result.IsSuccess && result.Value;
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

    private static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value, _jsonOptions);
    }
}
