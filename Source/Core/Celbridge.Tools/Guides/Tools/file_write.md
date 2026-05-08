---
name: file_write
description: Replace the entire content of a file (or create it) — use targeted edit tools for partial changes.
---

# file_write

Writes text content to a file, creating it if it does not exist. For existing files, the entire content is replaced. Use this for new files or when the whole file is being regenerated; for small targeted changes, prefer `file_apply_edits`, `file_find_replace`, or `file_delete_lines` — they are more precise and less likely to clobber concurrent edits.

## Parameters

- `fileResource` — resource key of the file. Created automatically if missing. Parent folders must already exist.
- `content` — the new text content. Line endings in `content` are written verbatim; the tool does not normalise.

## Returns

A JSON object with `lineCount` — the line count of the written content.

## See also

- `file_changes` — save model and how the editor reloads after the write.
- `editing_documents` — when to pick `file_write` vs. targeted edits.
- `file_apply_edits`, `file_find_replace`, `file_delete_lines` — partial-edit alternatives.
- `file_write_binary` — non-text content.
- `resource_keys`.
