using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by document_apply_edits with the affected line ranges and resulting line count.
/// </summary>
public record class ApplyEditsResult(List<AffectedLineRange> AffectedLines, int? TotalLineCount = null);

/// <summary>
/// A line range affected by a document edit, using 1-based line numbers.
/// ContextLines contains the post-edit content of the affected lines plus one
/// surrounding line on each side, allowing immediate verification without a
/// follow-up file_read call.
/// </summary>
public record class AffectedLineRange(int From, int To, List<string>? ContextLines = null);

public partial class DocumentTools
{
    /// <summary>
    /// Applies targeted text edits to a document at specific line and column positions.
    /// Each edit specifies a range and replacement text, using 1-based line and column numbers.
    /// Edits are applied as a single undo unit when routed through the editor.
    /// </summary>
    /// <param name="fileResource">Resource key of the file to edit.</param>
    /// <param name="editsJson">JSON array of edit objects, each with fields: line (int), column (int, optional, default 1), endLine (int), endColumn (int, optional, default -1), newText (string). Line and column numbers are 1-based. column defaults to 1 and endColumn defaults to -1 (end of line), so whole-line replacements only require line, endLine, and newText.</param>
    /// <param name="openDocument">When true (default), opens the document in the editor with undo support. When false and document is not already open, applies edits directly to the file on disk.</param>
    /// <returns>JSON object describing the document state after the edits are applied, with fields: affectedLines (array of objects with from (int), to (int), and contextLines (array of strings showing the post-edit content of the affected lines with one line of surrounding context on each side)), totalLineCount (int, post-edit line count). Use these fields to verify the edit landed without issuing a follow-up file_read.</returns>
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
            return SuccessResult("ok");
        }

        var documentEdit = new DocumentEdit(fileResourceKey, textEdits);

        // ForceSave so the follow-up disk read below sees the post-edit state.
        var applyResult = await ExecuteCommandAsync<IApplyEditsCommand>(command =>
        {
            command.Edits = new List<DocumentEdit> { documentEdit };
            command.OpenDocument = openDocument;
            command.ForceSave = true;
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
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
