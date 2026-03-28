namespace Celbridge.Tools;

/// <summary>
/// Result returned by file_read when offset or limit are specified.
/// </summary>
public record class FileReadResult(string Content, int TotalLineCount);

/// <summary>
/// Result returned by file_read_binary with base64-encoded file content.
/// </summary>
public record class FileReadBinaryResult(string Base64, string MimeType, int Size);

/// <summary>
/// Result returned by file_get_info for file resources.
/// </summary>
public record class FileInfoResult(string Type, long Size, string Modified, string Extension, bool IsText, int? LineCount);

/// <summary>
/// Result returned by file_get_info for folder resources.
/// </summary>
public record class FolderInfoResult(string Type, string Modified);

/// <summary>
/// A file entry in the file_list_contents output.
/// </summary>
public record class ListContentsFileItem(string Name, string Type, long Size, string Modified);

/// <summary>
/// A folder entry in the file_list_contents output.
/// </summary>
public record class ListContentsFolderItem(string Name, string Type, string Modified);

/// <summary>
/// A folder node in the file_get_tree output. Includes a truncated flag when the node
/// is at the depth limit and has children that were not expanded.
/// </summary>
public record class TreeFolderNode(string Name, string Type, List<object> Children, bool? Truncated = null);

/// <summary>
/// A file node in the file_get_tree output.
/// </summary>
public record class TreeFileNode(string Name, string Type);

/// <summary>
/// A file search result entry with metadata, returned by file_search when includeMetadata is true.
/// </summary>
public record class SearchResultWithMetadata(string Resource, long Size, string Modified);

/// <summary>
/// Top-level result returned by file_grep with match totals and per-file results.
/// </summary>
public record class GrepResult(int TotalMatches, int TotalFiles, bool Truncated, List<GrepFileResult> Files);

/// <summary>
/// Per-file result within a file_grep response. Content is included when includeContent is true.
/// </summary>
public record class GrepFileResult(string Resource, string FileName, List<object> Matches, string? Content = null);

/// <summary>
/// A single match within a file_grep result, without context lines.
/// </summary>
public record class GrepMatch(int LineNumber, string LineText, int MatchStart, int MatchLength);

/// <summary>
/// A single match within a file_grep result, with surrounding context lines.
/// </summary>
public record class GrepMatchWithContext(int LineNumber, string LineText, int MatchStart, int MatchLength, List<string> ContextBefore, List<string> ContextAfter);

/// <summary>
/// Top-level result returned by file_read_many.
/// </summary>
public record class ReadManyResult(List<ReadManyFileEntry> Files);

/// <summary>
/// A per-file entry in the file_read_many response. Contains either content on success or an error message on failure.
/// </summary>
public record class ReadManyFileEntry(string Resource, string? Content = null, int? TotalLineCount = null, string? Error = null);

/// <summary>
/// Result returned by document_write with the line count of the written content.
/// </summary>
public record class WriteDocumentResult(int LineCount);

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
