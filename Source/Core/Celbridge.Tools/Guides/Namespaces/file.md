# file

The `file` namespace operates on the contents of files in the project tree: reading, writing, searching, and applying targeted edits. The `explorer` namespace manages a file's existence, location, and name; `file` manages what is inside. Most edits write straight to disk and are visible immediately to the user — read `file_changes` before you edit.

## Must-knows

- **Edits write straight to disk.** `file_write`, `file_apply_edits`, `file_find_replace`, `file_delete_lines`, and `file_write_binary` save immediately. If the document is open, the editor reloads from disk and Monaco's undo history is wiped. The agent's edit cannot be reverted with Ctrl+Z. See `file_changes`.
- **Resource keys are forward-slash relative paths.** No backslashes, no absolute paths. See `resource_keys`.
- **`file_grep` and `file_find_replace` use .NET regex syntax.** See `regex_syntax`.
- **Prefer `file_apply_edits` over `file_write` for narrow changes.** `file_apply_edits` accepts a batch of (line, column, endLine, endColumn, replacement) edits as a single undo unit; `file_write` replaces the whole file and is more invasive.
- **Reading binary content takes a separate tool.** `file_read` returns text; `file_read_binary` returns base64. `file_read_image` returns an inline image content block for vision-capable models.

## Tools

**Reading.**

- `file_read` — read text content. Optional line range and encoding.
- `file_read_many` — read many files in one call (cheaper than N calls when scanning).
- `file_read_binary` — read raw bytes as base64.
- `file_read_image` — read an image inline as a vision content block.
- `file_get_info` — file metadata (size, mtime, hash) without reading content.
- `file_get_tree` — directory tree under a resource.
- `file_list_contents` — direct children of a folder.

**Searching.**

- `file_grep` — regex search across file contents.
- `file_search` — fuzzy / glob search by name.

**Writing.**

- `file_write` — replace a file's contents. Optional `createIfMissing`.
- `file_write_binary` — write raw bytes from base64.
- `file_apply_edits` — batch of targeted edits as one undo unit. Preferred for narrow changes.
- `file_find_replace` — pattern-based replace across a file.
- `file_delete_lines` — delete a range of lines.
