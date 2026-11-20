using System.Text.Json.Nodes;
using Celbridge.LanguageServers.Services;
using Celbridge.Resources;

namespace Celbridge.LanguageServers.Helpers;

/// <summary>
/// A workspace edit's text edits to one document. Version names the document's version when the server made the
/// edit, or is null when the server named no version.
/// </summary>
internal sealed record DocumentEdit(string Uri, int? Version, List<RangeEdit> Edits);

/// <summary>
/// Reads a server's workspace edit as range edits for each document. Lines and columns count from 1 in a range
/// edit, and from zero in the server's edit.
/// </summary>
internal static class WorkspaceEditReader
{
    // The property of a documentChanges item that creates, renames or deletes a file.
    private const string FileOperationKindProperty = "kind";

    /// <summary>
    /// Returns the edits to each document, in the edit's order. Fails when the edit has an unknown shape, or creates,
    /// renames or deletes a file.
    /// </summary>
    public static Result<List<DocumentEdit>> Read(JsonNode? editNode)
    {
        if (!LspJson.TryRead<WorkspaceEditBody>(editNode, out var edit))
        {
            return Result.Fail("The workspace edit has an unknown shape");
        }

        // A server may send both lists, and then documentChanges is the one to apply.
        if (edit.DocumentChanges is not null)
        {
            return ReadDocumentChanges(edit.DocumentChanges);
        }

        var documentEdits = new List<DocumentEdit>();
        if (edit.Changes is null)
        {
            return documentEdits;
        }

        foreach (var (uri, textEdits) in edit.Changes)
        {
            var rangeEditsResult = ToRangeEdits(textEdits);
            if (rangeEditsResult.IsFailure)
            {
                return Result.Fail(rangeEditsResult);
            }
            var rangeEdits = rangeEditsResult.Value;

            documentEdits.Add(new DocumentEdit(uri, null, rangeEdits));
        }

        return documentEdits;
    }

    private static Result<List<DocumentEdit>> ReadDocumentChanges(JsonArray documentChanges)
    {
        var documentEdits = new List<DocumentEdit>();

        foreach (var change in documentChanges)
        {
            if (change is JsonObject changeObject
                && changeObject.ContainsKey(FileOperationKindProperty))
            {
                return Result.Fail("The workspace edit creates, renames or deletes a file");
            }

            if (!LspJson.TryRead<TextDocumentEdit>(change, out var textDocumentEdit)
                || textDocumentEdit.TextDocument?.Uri is not string uri)
            {
                return Result.Fail("The workspace edit has an unknown shape");
            }

            var rangeEditsResult = ToRangeEdits(textDocumentEdit.Edits);
            if (rangeEditsResult.IsFailure)
            {
                return Result.Fail(rangeEditsResult);
            }
            var rangeEdits = rangeEditsResult.Value;

            var version = textDocumentEdit.TextDocument.Version;
            documentEdits.Add(new DocumentEdit(uri, version, rangeEdits));
        }

        return documentEdits;
    }

    private static Result<List<RangeEdit>> ToRangeEdits(IReadOnlyList<LspTextEdit>? textEdits)
    {
        var rangeEdits = new List<RangeEdit>();
        if (textEdits is null)
        {
            return rangeEdits;
        }

        foreach (var textEdit in textEdits)
        {
            if (textEdit.Range is not LspRange range
                || textEdit.NewText is not string newText)
            {
                return Result.Fail("A text edit in the workspace edit has an unknown shape");
            }

            var rangeEdit = new RangeEdit(
                range.Start.Line + 1,
                range.Start.Character + 1,
                range.End.Line + 1,
                range.End.Character + 1,
                newText);
            rangeEdits.Add(rangeEdit);
        }

        return rangeEdits;
    }
}
