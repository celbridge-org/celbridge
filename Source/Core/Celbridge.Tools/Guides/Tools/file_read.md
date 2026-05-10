# file_read

Reads the text content of a file and returns it along with the file's total line count. The default reads the whole file; pass `offset` and `limit` to page large files. For multiple files in one call, use `file_read_many`. For very large files, call `file_get_info` first to check `lineCount` and `size` before deciding whether to page.

## Parameters

### offset

1-based line number to start reading from. `0` (default) starts at the beginning. `offset: 100` skips the first 99 lines and starts at line 100.

### limit

Maximum number of lines to return. `0` (default) reads to the end of the file from `offset`.

### lineNumbers

When `true`, each line in `content` is prefixed with its 1-based line number, e.g. `"42: ..."`. Numbers reflect the line's actual position in the file, so a paged read still shows true line numbers — convenient when you plan to follow up with `file_apply_edits` or `file_delete_lines`.

## Returns

A JSON object with:

- `content` — the requested text. Raw line endings from disk are preserved when `lineNumbers` is false; when prefixing is on the tool emits one separator per line using the file's detected line ending.
- `totalLineCount` — line count of the entire file, not just the returned range.

When `offset` is past the end of the file, `content` comes back as the empty string and `totalLineCount` still reflects the file.
