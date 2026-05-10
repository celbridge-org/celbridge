# file_write

Writes text content to a file, creating it if it does not exist. For existing files, the entire content is replaced. Use this for new files or when the whole file is being regenerated; for small targeted changes, prefer `file_apply_edits`, `file_find_replace`, or `file_delete_lines` — they are more precise and less likely to clobber concurrent edits.

## Parameters

- `fileResource` — resource key of the file. Created automatically if missing. Parent folders must already exist.
- `content` — the new text content. Line endings are written verbatim; the tool does not normalise.

## Returns

A JSON object with `lineCount` — the line count of the written content.
