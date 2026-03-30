using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// MCP tools for document content editing and editor management.
/// </summary>
[McpServerToolType]
public partial class DocumentTools : AgentToolBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DocumentTools(IApplicationServiceProvider services) : base(services) { }

    /// <summary>
    /// Opens a document in the editor. By default the document is opened without
    /// activating it, so the user's current active tab is preserved. Use
    /// document_activate to bring a document to the foreground.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to open.</param>
    /// <param name="sectionIndex">Target editor section: 0 (left), 1 (center), 2 (right). Use -1 to open in the active section (default).</param>
    /// <param name="forceReload">Force reload even if already open.</param>
    /// <param name="activate">When true, the opened document becomes the active tab.</param>
    [McpServerTool(Name = "document_open", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.open")]
    public async partial Task<CallToolResult> Open(string fileResource, int sectionIndex = -1, bool forceReload = false, bool activate = false)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        if (sectionIndex != -1 && sectionIndex is < 0 or > 2)
        {
            return ErrorResult($"Invalid sectionIndex '{sectionIndex}': must be 0, 1, 2, or -1 for the active section.");
        }

        int? targetSectionIndex = sectionIndex == -1 ? null : sectionIndex;

        return await ExecuteCommandAsync<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.TargetSectionIndex = targetSectionIndex;
            command.ForceReload = forceReload;
            command.Activate = activate;
        });
    }

    /// <summary>
    /// Closes one or more documents in the editor.
    /// Pass a single resource key or a JSON array of resource keys (e.g. ["foo.txt","scripts/bar.txt"]).
    /// Documents are closed sequentially; if any close fails (e.g. user cancels a save prompt), the remaining documents are still attempted.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to close, or a JSON array of resource keys.</param>
    /// <param name="forceClose">Force close without save confirmation.</param>
    /// <returns>JSON object with fields: closed (int), failed (int), errors (array of strings).</returns>
    [McpServerTool(Name = "document_close", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.close")]
    public async partial Task<CallToolResult> Close(string fileResource, bool forceClose = false)
    {
        var resourceKeyStrings = ParseResourceKeys(fileResource);

        var validatedKeys = new List<ResourceKey>();
        foreach (var keyString in resourceKeyStrings)
        {
            if (!ResourceKey.TryCreate(keyString, out var validatedKey))
            {
                return ErrorResult($"Invalid resource key: '{keyString}'");
            }
            validatedKeys.Add(validatedKey);
        }

        int closedCount = 0;
        var errors = new List<string>();

        foreach (var resourceKey in validatedKeys)
        {
            var result = await CommandService.ExecuteAsync<ICloseDocumentCommand>(command =>
            {
                command.FileResource = resourceKey;
                command.ForceClose = forceClose;
            });

            if (result.IsSuccess)
            {
                closedCount++;
            }
            else
            {
                errors.Add($"{resourceKey}: {result.FirstErrorMessage}");
            }
        }

        var summary = new
        {
            closed = closedCount,
            failed = errors.Count,
            errors
        };

        if (errors.Count > 0)
        {
            return ErrorResult(JsonSerializer.Serialize(summary));
        }

        return SuccessResult(JsonSerializer.Serialize(summary));
    }

    private static List<string> ParseResourceKeys(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.StartsWith('['))
        {
            var keys = JsonSerializer.Deserialize<List<string>>(trimmed);
            return keys ?? new List<string> { input };
        }

        return new List<string> { input };
    }

    /// <summary>
    /// Gets all open documents with their editor position, active state, and unsaved changes flag.
    /// </summary>
    /// <returns>JSON array of objects with fields: resource (string), sectionIndex (int), tabOrder (int), isActive (bool).</returns>
    [McpServerTool(Name = "document_get_open", ReadOnly = true)]
    [ToolAlias("document.get_open")]
    public partial CallToolResult GetOpen()
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        var openDocuments = documentsService.OpenDocumentAddresses;
        var activeDocument = documentsService.ActiveDocument;

        var documents = new List<object>();
        foreach (var (resource, address) in openDocuments)
        {
            documents.Add(new
            {
                resource = resource.ToString(),
                sectionIndex = address.SectionIndex,
                tabOrder = address.TabOrder,
                isActive = resource == activeDocument
            });
        }

        return SuccessResult(JsonSerializer.Serialize(documents));
    }

    /// <summary>
    /// Activates an open document, making it the active tab in the editor.
    /// The document must already be open.
    /// </summary>
    /// <param name="fileResource">Resource key of the document to activate.</param>
    [McpServerTool(Name = "document_activate", ReadOnly = false, Idempotent = true)]
    [ToolAlias("document.activate")]
    public async partial Task<CallToolResult> Activate(string fileResource)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        return await ExecuteCommandAsync<IActivateDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
        });
    }

    /// <summary>
    /// Applies targeted text edits to a document at specific line and column positions.
    /// Each edit specifies a range and replacement text, using 1-based line and column numbers.
    /// Edits are applied as a single undo unit when routed through the editor.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to edit.</param>
    /// <param name="editsJson">JSON array of edit objects, each with fields: line (int), column (int, optional, default 1), endLine (int), endColumn (int, optional, default -1), newText (string). Line and column numbers are 1-based. column defaults to 1 and endColumn defaults to -1 (end of line), so whole-line replacements only require line, endLine, and newText.</param>
    /// <param name="openDocument">When true (default), opens the document in the editor with undo support. When false and document is not already open, applies edits directly to the file on disk.</param>
    /// <returns>JSON object with fields: affectedLines (array of objects with from (int), to (int), and contextLines (array of strings showing the affected lines with one line of surrounding context)), totalLineCount (int).</returns>
    [McpServerTool(Name = "document_apply_edits")]
    [ToolAlias("document.apply_edits")]
    public async partial Task<CallToolResult> ApplyEdits(string fileResource, string editsJson, bool openDocument = true)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        List<TextEdit> textEdits;
        try
        {
            textEdits = ParseEditsJson(editsJson);
        }
        catch (JsonException ex)
        {
            return ErrorResult($"Invalid edits JSON: {ex.Message}");
        }

        if (textEdits.Count == 0)
        {
            return new CallToolResult();
        }

        var documentEdit = new DocumentEdit(fileResourceKey, textEdits);

        var applyResult = await ExecuteCommandAsync<IApplyEditsCommand>(command =>
        {
            command.Edits = new List<DocumentEdit> { documentEdit };
            command.OpenDocument = openDocument;
        });

        if (applyResult.IsError == true)
        {
            return applyResult;
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var affectedLines = new List<AffectedLineRange>();
        int? totalLineCount = null;

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResourceKey);
        if (resolveResult.IsSuccess && File.Exists(resolveResult.Value))
        {
            var fileLines = await File.ReadAllLinesAsync(resolveResult.Value);
            totalLineCount = fileLines.Length;

            foreach (var edit in textEdits.OrderBy(e => e.Line))
            {
                var contextStartIndex = Math.Max(0, edit.Line - 2);
                var contextEndIndex = Math.Min(fileLines.Length - 1, edit.EndLine);
                var contextLines = fileLines
                    .Skip(contextStartIndex)
                    .Take(contextEndIndex - contextStartIndex + 1)
                    .ToList();
                affectedLines.Add(new AffectedLineRange(edit.Line, edit.EndLine, contextLines));
            }
        }
        else
        {
            foreach (var edit in textEdits.OrderBy(e => e.Line))
            {
                affectedLines.Add(new AffectedLineRange(edit.Line, edit.EndLine));
            }
        }

        var result = new ApplyEditsResult(affectedLines, totalLineCount);
        return SuccessResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    /// <summary>
    /// Deletes complete lines from a document, removing them entirely including their
    /// line terminators. Unlike document_apply_edits with empty newText (which always
    /// leaves a residual empty line), this tool cleanly removes the specified lines.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to delete lines from.</param>
    /// <param name="startLine">First line to delete (1-based, inclusive).</param>
    /// <param name="endLine">Last line to delete (1-based, inclusive).</param>
    /// <param name="openDocument">When true (default), opens the document in the editor with undo support. When false and document is not already open, deletes lines directly from the file on disk.</param>
    /// <returns>JSON with fields: deletedFrom (int), deletedTo (int), totalLineCount (int), contextLines (array of strings around the deletion point).</returns>
    [McpServerTool(Name = "document_delete_lines")]
    [ToolAlias("document.delete_lines")]
    public async partial Task<CallToolResult> DeleteLines(string fileResource, int startLine, int endLine, bool openDocument = true)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        if (startLine < 1)
        {
            return ErrorResult($"startLine must be at least 1, got {startLine}");
        }

        if (endLine < startLine)
        {
            return ErrorResult($"endLine ({endLine}) must be greater than or equal to startLine ({startLine})");
        }

        var deleteResult = await ExecuteCommandAsync<IDeleteLinesCommand>(command =>
        {
            command.Resource = fileResourceKey;
            command.StartLine = startLine;
            command.EndLine = endLine;
            command.OpenDocument = openDocument;
        });

        if (deleteResult.IsError == true)
        {
            return deleteResult;
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        List<string>? contextLines = null;
        int totalLineCount = 0;

        var resolveResult = resourceRegistry.ResolveResourcePath(fileResourceKey);
        if (resolveResult.IsSuccess && File.Exists(resolveResult.Value))
        {
            var fileLines = await File.ReadAllLinesAsync(resolveResult.Value);
            totalLineCount = fileLines.Length;

            // Show a few lines around the deletion point for verification
            var deletionPoint = Math.Min(startLine - 1, fileLines.Length);
            var contextStart = Math.Max(0, deletionPoint - 1);
            var contextEnd = Math.Min(fileLines.Length - 1, deletionPoint + 1);

            if (fileLines.Length > 0)
            {
                contextLines = fileLines
                    .Skip(contextStart)
                    .Take(contextEnd - contextStart + 1)
                    .ToList();
            }
        }

        var result = new DeleteLinesResult(startLine, endLine, totalLineCount, contextLines);
        return SuccessResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    /// <summary>
    /// Writes text content to a document. Creates the file if it does not exist.
    /// For existing files, replaces the entire content.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to write. The file is created automatically if it does not exist.</param>
    /// <param name="content">The new text content for the document.</param>
    /// <param name="openDocument">When true (default), opens the document in the editor with undo support. When false and document is not already open, writes directly to disk.</param>
    /// <returns>JSON object with field: lineCount (int).</returns>
    [McpServerTool(Name = "document_write")]
    [ToolAlias("document.write")]
    public async partial Task<CallToolResult> Write(string fileResource, string content, bool openDocument = true)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        var writeResult = await ExecuteCommandAsync<IWriteDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Content = content;
            command.OpenDocument = openDocument;
        });

        if (writeResult.IsError == true)
        {
            return writeResult;
        }

        var lineCount = content.Split('\n').Length;
        var result = new WriteDocumentResult(lineCount);
        return SuccessResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    /// <summary>
    /// Replaces the content of a binary document from base64-encoded data.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to write.</param>
    /// <param name="base64Content">The new content as a base64-encoded string.</param>
    /// <param name="openDocument">When true (default), opens the document in the editor. When false and document is not already open, writes decoded bytes directly to disk.</param>
    [McpServerTool(Name = "document_write_binary")]
    [ToolAlias("document.write_binary")]
    public async partial Task<CallToolResult> WriteBinary(string fileResource, string base64Content, bool openDocument = true)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        return await ExecuteCommandAsync<IWriteBinaryDocumentCommand>(command =>
        {
            command.FileResource = fileResourceKey;
            command.Base64Content = base64Content;
            command.OpenDocument = openDocument;
        });
    }

    /// <summary>
    /// Finds and replaces text within a document. Supports plain text and regex patterns.
    /// Multi-line search and replace text may use \n line endings regardless of the file's
    /// actual line endings — the tool normalises them automatically.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to perform find and replace on.</param>
    /// <param name="searchText">The text to search for.</param>
    /// <param name="replaceText">The replacement text.</param>
    /// <param name="matchCase">If true, the search is case-sensitive.</param>
    /// <param name="useRegex">If true, the search text is treated as a regular expression.</param>
    /// <param name="openDocument">When true (default), opens the document in the editor with undo support. When false and document is not already open, applies replacements directly to the file on disk.</param>
    /// <param name="fromLine">First line number (1-based, inclusive) to include in the replacement scope. Zero (default) means no lower bound.</param>
    /// <param name="toLine">Last line number (1-based, inclusive) to include in the replacement scope. Zero (default) means no upper bound.</param>
    /// <returns>JSON object with field: replacementCount (int).</returns>
    [McpServerTool(Name = "document_find_replace")]
    [ToolAlias("document.find_replace")]
    public async partial Task<CallToolResult> FindReplace(
        string fileResource,
        string searchText,
        string replaceText,
        bool matchCase = false,
        bool useRegex = false,
        bool openDocument = true,
        int fromLine = 0,
        int toLine = 0)
    {
        if (!ResourceKey.TryCreate(fileResource, out var fileResourceKey))
        {
            return ErrorResult($"Invalid resource key: '{fileResource}'");
        }

        var (callResult, replacementCount) = await ExecuteCommandAsync<IFindReplaceDocumentCommand, int>(command =>
        {
            command.FileResource = fileResourceKey;
            command.SearchText = searchText;
            command.ReplaceText = replaceText;
            command.MatchCase = matchCase;
            command.UseRegex = useRegex;
            command.OpenDocument = openDocument;
            command.FromLine = fromLine;
            command.ToLine = toLine;
        });

        if (callResult.IsError == true)
        {
            return callResult;
        }

        return SuccessResult(JsonSerializer.Serialize(new { replacementCount }));
    }

    private static List<TextEdit> ParseEditsJson(string editsJson)
    {
        var edits = new List<TextEdit>();
        var jsonDocument = JsonDocument.Parse(editsJson);

        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            var line = element.GetProperty("line").GetInt32();
            var column = element.TryGetProperty("column", out var columnElement) ? columnElement.GetInt32() : 1;
            var endLine = element.GetProperty("endLine").GetInt32();
            var endColumn = element.TryGetProperty("endColumn", out var endColumnElement) ? endColumnElement.GetInt32() : -1;
            var newText = element.GetProperty("newText").GetString() ?? string.Empty;

            edits.Add(new TextEdit(line, column, endLine, endColumn, newText));
        }

        return edits;
    }
}
